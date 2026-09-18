using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record RenewalLifecycleView(Guid PolicyId,Guid TermId,string Etag,Guid RuleSettingVersionId,RenewalTimeline Timeline,
    string State,bool CanLapse,Guid? LapseEventId,string? LapseReason,string? LapseMode,DateTimeOffset? RecordedAt,string? NotificationState,
    IReadOnlyList<RenewalNotificationAttempt> NotificationAttempts);
public sealed record RenewalNotificationAttempt(int Number,DateTimeOffset StartedAt,DateTimeOffset? EndedAt,string Outcome,string? ErrorCode);

public sealed class RenewalLifecycleService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string NotificationKind="renewal-lapse-notification";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly SqlCommandBoundary commands=new(factory,time);

    public async Task<RenewalLifecycleView> ReadAsync(ActorContext actor,Guid termId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var term=await Hold(db,termId,actor,false,token);
        var lapse=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.TermId==termId,token);
        var (setting,timeline)=await Timeline(db,term,time.GetUtcNow(),token,lapse?.RuleSettingVersionId);
        var state=lapse is not null?"lapsed":await Stage(db,term,timeline,time.GetUtcNow(),token);
        var notification=lapse is null?null:await db.Set<OutboxWork>().Where(x=>x.Id==lapse.WorkId).Select(x=>x.State).SingleAsync(token);
        var attempts=lapse is null?[]:await db.Set<AdapterAttempt>().AsNoTracking().Where(x=>x.WorkId==lapse.WorkId).OrderBy(x=>x.AttemptNumber)
            .Take(18).Select(x=>new RenewalNotificationAttempt(x.AttemptNumber,x.StartedAt,x.EndedAt,x.Outcome,x.ErrorCode)).ToArrayAsync(token);
        var result=new RenewalLifecycleView(term.PolicyId,termId,Etag(term),setting.Id,timeline,state,state is not("lapsed" or "accepted" or "issued" or "cancelled"),
            lapse?.Id,lapse?.Reason,lapse?.Mode,lapse?.CreatedAt,notification,attempts);
        await tx.CommitAsync(token);return result;
    }

    public Task<CommandOutcome> LapseAsync(ActorContext actor,Guid termId,byte[] version,string reason,string key,Guid correlation,CancellationToken token=default)
    {
        if(termId==Guid.Empty || version is null || version.Length!=8)throw new QuoteOperationException(400,"renewal-lapse-input-invalid");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<10 || reason.Length>1000 || reason.Any(char.IsControl))
            throw new QuoteOperationException(422,"renewal-lapse-reason-required");
        reason=reason.Trim();PolicyTerm? held=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/terms/{termId:D}/lapse",key,correlation),
            new{termId,version=Convert.ToBase64String(version),reason},"renewal.lapse-recorded",
            async(db,ct)=>held=await Hold(db,termId,actor,true,ct),
            async(db,ct)=>
            {
                var prior=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.TermId==termId,ct);
                if(prior is not null)return Receipt(held!,prior,200);
                if(!CryptographicOperations.FixedTimeEquals(version,held!.RowVersion))throw new QuoteOperationException(412,"renewal-term-version-conflict");
                var row=await Apply(db,held,actor.UserId,reason,false,correlation,time.GetUtcNow(),ct)
                    ??throw new QuoteOperationException(409,"renewal-lapse-ineligible");
                return Receipt(held,row,201);
            },token);
    }

    public async Task<Guid?> LapseDueAsync(Guid termId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var term=await Hold(db,termId,null,true,token);
        var prior=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.TermId==termId,token);
        var row=prior??await Apply(db,term,null,"Automatic lapse after the configured renewal acceptance deadline",true,Guid.NewGuid(),time.GetUtcNow(),token);
        await tx.CommitAsync(token);return row?.Id;
    }

    private static async Task<PolicyTerm> Hold(BackOfficeDbContext db,Guid termId,ActorContext? actor,bool write,CancellationToken token)
    {
        var hint=await (from t in db.Set<PolicyTerm>() join p in db.Set<Policy>() on t.PolicyId equals p.Id where t.Id==termId select new{p.Id,p.SourceQuoteId,p.AgencyId})
            .SingleOrDefaultAsync(token)??throw new QuoteOperationException(404,"policy-term-not-found");
        if(actor is not null)
        {
            var source=await QuoteScope.ForQuoteAsync(db,actor,hint.SourceQuoteId,write?QuoteAccess.Underwriting:QuoteAccess.Read,token);
            await PolicyScope.Hold(db,source.Scope.Actor,hint.Id,token,write);
        }
        else
        {
            // Match the agency -> quote -> policy -> term order of acceptance
            // and issue, without impersonating a human for scheduled work.
            _=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.AgencyId}").AsNoTracking().SingleAsync(token);
            _=await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.SourceQuoteId}").AsNoTracking().SingleAsync(token);
            _=await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.Id}").AsNoTracking().SingleAsync(token);
        }
        return write?await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={termId}").SingleAsync(token)
            :await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(HOLDLOCK) WHERE Id={termId}").SingleAsync(token);
    }

    private static async Task<(SettingVersion,RenewalTimeline)> Timeline(BackOfficeDbContext db,PolicyTerm term,DateTimeOffset now,CancellationToken token,Guid? pinnedSetting=null)
    {
        var settings=await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope={RenewalConfiguration.Scope}").AsNoTracking().ToArrayAsync(token);
        var setting=pinnedSetting is {} id?settings.SingleOrDefault(x=>x.Id==id):settings.Where(x=>x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefault();
        var rule=setting is null?null:RenewalConfiguration.Parse(setting.Values);
        if(setting is null || rule is null)throw new QuoteOperationException(503,"renewal-configuration-unavailable");
        try{return(setting,RenewalLifecycleRules.Timeline(term.EndsAt,rule.InvitationDaysBeforeExpiry,rule.LapseDaysAfterExpiry));}
        catch(ArgumentException){throw new QuoteOperationException(409,"renewal-timeline-ambiguous");}
    }

    private static async Task<string> Stage(BackOfficeDbContext db,PolicyTerm term,RenewalTimeline timeline,DateTimeOffset now,CancellationToken token)
    {
        if(await db.Set<PolicyTerm>().AnyAsync(x=>x.PolicyId==term.PolicyId && x.Id!=term.Id && x.StartsAt==term.EndsAt,token))return "issued";
        var last=await (from v in db.Set<PolicyVersion>() join t in db.Set<PolicyTransaction>() on v.TransactionId equals t.Id
            where v.TermId==term.Id && v.EffectiveAt<term.EndsAt && v.ProcessedAt<=now
            orderby v.EffectiveAt descending,t.Sequence descending,v.SliceOrdinal descending select t.Kind).FirstOrDefaultAsync(token);
        if(last is null or "cancellation")return "cancelled";
        var cycles=from d in db.Set<ServicingDraft>() join c in db.Set<ServicingCycle>() on d.CurrentCycleId equals c.Id
            where d.BaseTermId==term.Id && d.Kind=="renewal" && d.State=="draft" && c.State=="rated" select c;
        if(await cycles.AnyAsync(x=>x.CurrentAcceptanceId!=null,token))return "accepted";
        if(now>=term.EndsAt)return "overdue";
        if(await (from c in cycles join delivery in db.Set<ServicingTermsDelivery>() on c.CurrentDeliveryId equals delivery.Id where delivery.State=="delivered" select delivery.Id).AnyAsync(token))return "invited";
        return now>=term.EndsAt?"overdue":now>=timeline.InvitationDueAt?"due":"not-due";
    }

    private static async Task<RenewalLapseEvent?> Apply(BackOfficeDbContext db,PolicyTerm term,Guid? actor,string reason,bool automatic,Guid correlation,DateTimeOffset now,CancellationToken token)
    {
        var (setting,timeline)=await Timeline(db,term,now,token);var state=await Stage(db,term,timeline,now,token);
        if(state is "accepted" or "issued" or "cancelled" || automatic && now<timeline.AutoLapseAt)return null;
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==term.PolicyId,token);
        var contacts=await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK) WHERE ClientId={policy.ClientId} AND RelationshipId={policy.RelationshipId}")
            .AsNoTracking().Where(x=>x.EndedAt==null).OrderBy(x=>x.Id).ToArrayAsync(token);
        var recipients=contacts.Where(x=>!string.IsNullOrWhiteSpace(x.DeclaredFullName) && x.Email is not null && !x.Email.Any(char.IsControl) &&
            System.Net.Mail.MailAddress.TryCreate(x.Email,out var email) && email.Address==x.Email).Select(x=>new ServicingTermsRecipient(x.Id,x.DeclaredFullName,x.Email!)).ToArray();
        var lapse=new RenewalLapseEvent{PolicyId=policy.Id,TermId=term.Id,RuleSettingVersionId=setting.Id,Mode=automatic?"automatic":"manual",Reason=reason,
            EffectiveAt=term.EndsAt,AutoLapseAt=timeline.AutoLapseAt,RecipientSnapshotJson=JsonSerializer.Serialize(recipients,Json),CreatedBy=actor,CreatedAt=now};
        var work=new OutboxWork{Kind=NotificationKind,SubjectRecordId=lapse.Id,OperationKey=$"renewal-lapse/{term.Id:N}",ScenarioVersionId=setting.Id,
            Payload=JsonSerializer.Serialize(new{format="renewal-lapse-notification-1",eventId=lapse.Id,policyId=policy.Id,termId=term.Id,policyReference=policy.Reference,
                effectiveAt=term.EndsAt,reason,recipients,demo=true},Json),NextAttemptAt=now,CreatedAt=now,UpdatedAt=now,CreatedBy=actor,CorrelationId=correlation};
        db.Add(work);await db.SaveChangesAsync(token);lapse.WorkId=work.Id;db.Add(lapse);
        var drafts=await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE BaseTermId={term.Id} AND Kind=N'renewal' AND State=N'draft'").ToArrayAsync(token);
        foreach(var draft in drafts)
        {
            draft.UpdatedAt=now;db.Entry(draft).Property(x=>x.UpdatedAt).IsModified=true;
            var lease=await db.Set<ServicingLease>().SingleOrDefaultAsync(x=>x.DraftId==draft.Id,token);
            if(lease is not null){lease.Active=false;lease.ExpiresAt=now;}
        }
        term.UpdatedAt=now;db.Entry(term).Property(x=>x.UpdatedAt).IsModified=true;
        db.Add(new AuditEvent{SubjectRecordId=term.Id,ActorId=actor,CreatedBy=actor,CreatedAt=now,OccurredAt=now,EventType="policy.renewal-lapsed",Reason=reason,CorrelationId=correlation,
            After=JsonSerializer.Serialize(new{eventId=lapse.Id,policyId=policy.Id,termId=term.Id,effectiveAt=term.EndsAt,mode=lapse.Mode,workId=work.Id})});
        await db.SaveChangesAsync(token);return lapse;
    }

    private static string Etag(PolicyTerm term)=>"\""+Convert.ToBase64String(term.RowVersion)+"\"";
    private static CommandOutcome Receipt(PolicyTerm term,RenewalLapseEvent row,int status)=>new(row.Id,status,
        JsonSerializer.Serialize(new{id=row.Id,policyId=row.PolicyId,termId=row.TermId,termEtag=Etag(term),effectiveAt=row.EffectiveAt,recordedAt=row.CreatedAt,mode=row.Mode,notificationId=row.WorkId},Json),Etag:Etag(term));
}
