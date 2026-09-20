using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record RenewalPreparationPreview(Guid PolicyId,Guid ExpiringTermId,Guid BaseVersionId,string TermEtag,
    ResolvedQuoteTerm Term,JsonElement TermIntent,Guid ProductVersionId,Guid BinderVersionId,Guid AgencyTermsVersionId,
    Guid RuleSettingVersionId,string RuleVersion,Guid? FairValueAssessmentId,Guid? FairValueEvidenceFileId,bool FairValueSatisfied,
    string FairValueState,string BrokerArrearsState);

internal sealed record HeldRenewalEligibility(PolicyTerm ExpiringTerm,PolicyVersion Basis,RenewalPreparedTerm Prepared,
    SettingVersion Setting,RenewalSettings Settings,EligibleQuoteRating Eligible,FairValueAssessmentVersion? FairValue,bool FairValueSatisfied);

public sealed partial class RenewalPreparationService
{
    public async Task<RenewalPreparationPreview> PreviewAsync(ActorContext actor,Guid termId,int? months=null,int? endOffsetMinutes=null,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var hint=await (from t in db.Set<PolicyTerm>() join p in db.Set<Policy>() on t.PolicyId equals p.Id
            where t.Id==termId select new{p.Id,p.SourceQuoteId}).SingleOrDefaultAsync(token)
            ??throw new QuoteOperationException(404,"policy-term-not-found");
        var source=await QuoteScope.ForQuoteAsync(db,actor,hint.SourceQuoteId,QuoteAccess.Read,token);
        await PolicyScope.Hold(db,source.Scope.Actor,hint.Id,token);
        var held=await ResolveEligibility(db,source,termId,months,endOffsetMinutes,time.GetUtcNow(),token);
        var result=Preview(held);
        await tx.CommitAsync(token);return result;
    }

    internal static async Task<HeldRenewalEligibility> ResolveEligibility(BackOfficeDbContext db,OwnedQuoteScope source,Guid termId,
        int? months,int? endOffsetMinutes,DateTimeOffset now,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Renewal eligibility requires held policy scope.");
        var term=await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(HOLDLOCK) WHERE Id={termId}").SingleOrDefaultAsync(token)
            ??throw new QuoteOperationException(404,"policy-term-not-found");
        if(!await db.Set<Policy>().AnyAsync(x=>x.Id==term.PolicyId && x.SourceQuoteId==source.Quote.Id,token))throw new QuoteOperationException(404,"policy-term-not-found");
        if(await db.Set<RenewalLapseEvent>().AnyAsync(x=>x.TermId==term.Id,token))throw new QuoteOperationException(409,"renewal-already-lapsed");
        var settingScope=await SettingsScope(db,term.ProductId,token);
        var settings=await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope={settingScope}").AsNoTracking().ToArrayAsync(token);
        var setting=settings.Where(x=>x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefault();
        var config=setting is null?null:RenewalConfiguration.Parse(setting.Values,settingScope);
        if(setting is null || config is null)throw new QuoteOperationException(503,"renewal-configuration-unavailable");
        RenewalPreparedTerm prepared;
        try{prepared=RenewalPreparationRules.Term(term.EndsAt,months??config.DefaultTermMonths,config.AllowedTermMonths,endOffsetMinutes);}
        catch(ArgumentException){throw new QuoteOperationException(422,"renewal-term-invalid");}
        var candidates=await PolicyTemporalSelector.Candidates(db,term.PolicyId,now).ToArrayAsync(token);
        var terms=await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(HOLDLOCK) WHERE PolicyId={term.PolicyId}").AsNoTracking().ToArrayAsync(token);
        var selected=PolicyTemporalSelector.RenewalBase(candidates,term,prepared.Term.EndsAt,terms,now);
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==selected.VersionId && x.TermId==termId && x.PolicyId==term.PolicyId,token);
        var captureSettings=await QuoteCaptureEligibility.LoadSettingsAsync(db,now,token);
        var versions=await db.Set<ProductVersion>().FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(HOLDLOCK) WHERE ProductId={term.ProductId}").AsNoTracking().ToArrayAsync(token);
        var productVersion=versions.Where(x=>x.State=="published" && x.EffectiveFrom<=now && (x.EffectiveTo is null || now<x.EffectiveTo) &&
            x.EffectiveFrom<=prepared.Term.StartsAt && (x.EffectiveTo is null || prepared.Term.EndsAt<=x.EffectiveTo) &&
            captureSettings.Products.ContainsKey(x.Id) && captureSettings.DistributedProducts.Contains(x.Id)).OrderByDescending(x=>x.Version).FirstOrDefault()
            ??throw new QuoteOperationException(409,"renewal-product-unavailable");
        var inceptionDay=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(prepared.Term.StartsAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var capture=await QuoteCaptureEligibility.ResolveAsync(db,source.Scope,productVersion.Id,now,null,token,commercialOn:inceptionDay);
        var eligible=await QuoteRatingEligibility.ResolveAsync(db,source,productVersion.Id,capture.Terms.Id,prepared.Term,now,token,commercialOn:inceptionDay);
        // Never silently use today's commission when a different approved terms
        // version takes effect before renewal inception.
        var commercial=await db.Set<AgencyTermsVersion>().FromSqlInterpolated($"SELECT * FROM AgencyTermsVersion WITH(HOLDLOCK) WHERE AgencyId={source.Scope.Agency.Id}").AsNoTracking().ToArrayAsync(token);
        var applicable=commercial.Where(x=>x.EffectiveFrom<=inceptionDay).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Version).FirstOrDefault();
        if(applicable?.Id!=eligible.Capture.Terms.Id)throw new QuoteOperationException(409,"renewal-commercial-terms-refresh-required");
        var assessments=await db.Set<FairValueAssessmentVersion>().FromSqlInterpolated($"SELECT * FROM FairValueAssessmentVersion WITH(HOLDLOCK) WHERE ProductVersionId={productVersion.Id} AND BinderVersionId={eligible.BinderVersion.Id}").AsNoTracking().ToArrayAsync(token);
        var assessment=assessments.Where(x=>x.ApprovedAt<=now && x.ValidFrom<=prepared.Term.StartsAt && prepared.Term.StartsAt<x.ValidTo)
            .OrderByDescending(x=>x.ApprovedAt).ThenBy(x=>x.Outcome=="pass").ThenBy(x=>x.Id).FirstOrDefault();
        var satisfied=assessment?.Outcome=="pass" && await db.Set<ProductEvidenceFileVersion>().AnyAsync(x=>x.Id==assessment.EvidenceFileVersionId && x.ScreeningState=="accepted",token);
        return new(term,basis,prepared,setting,config,eligible,assessment,satisfied);
    }

    internal static async Task<string> SettingsScope(BackOfficeDbContext db,Guid productId,CancellationToken token)
    {
        var code=await db.Set<Product>().Where(x=>x.Id==productId).Select(x=>x.Code).SingleAsync(token);
        return code switch
        {
            "commercial-combined"=>RenewalConfiguration.CommercialScope,
            "motor-trade-road-risks" or "motor-trade-combined"=>RenewalConfiguration.Scope,
            _=>throw new QuoteOperationException(409,"renewal-product-unavailable")
        };
    }
}
