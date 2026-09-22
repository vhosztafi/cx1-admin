using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static Task<byte[]> TermsVersion(BackOfficeDbContext db, DecisionFixture f) => db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();

    private static async Task<Guid> TermsProof(BackOfficeDbContext db, DecisionFixture f, UnderwritingProofRequirement purpose, bool review = true)
    {
        var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
        var upload = await evidence.UploadAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f), "quotation-proof.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional actual supplied " + purpose.Code), Guid.NewGuid().ToString(), Guid.NewGuid());
        var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await TermsVersion(db, f), upload.ResourceId, purpose.Code, purpose.RiskItemId,
            purpose.ConditionId, purpose.TermsVersionId, purpose.InputFingerprint, "Actual supplied quotation proof", Guid.NewGuid().ToString(), Guid.NewGuid());
        if (review)
        {
            var association = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, association.Id, await TermsVersion(db, f), association.RowVersion, "accepted", purpose.InputFingerprint, "Reviewed actual quotation evidence", Guid.NewGuid().ToString(), Guid.NewGuid());
        }
        return attached.ResourceId;
    }

    private static async Task<(DecisionFixture Fixture, Guid TemplateId, Guid RatingId, Guid ContactId)> TermsFixture(BackOfficeDbContext db, string password, string product = "motor-trade-road-risks", bool tradingReferral = false)
    {
        var f = await ReadyUnderwriting(db, password, proposal => {
            if (tradingReferral) proposal["risk"]!["business"]!["startedOn"] = "2025-01-01";
            if (product == "motor-trade-combined")
                foreach (var section in proposal["cover"]!["requestedSections"]!.AsArray())
                    if (section!["code"]!.GetValue<string>() == "stock-custody") section["limit"] = "100000.00";
        }, productCode: product);
        var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
        foreach (var purpose in await evidence.RequirementsAsync(f.Servicing, f.QuoteId)) await TermsProof(db, f, purpose);
        var referrals = await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == f.CycleId).ToArrayAsync();
        if (referrals.Length > 0) await new QuoteReferralService(f.Factory, f.Clock).DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await TermsVersion(db, f),
            referrals.Select(x => new ReferralDecisionInput(x.Id, x.RowVersion, "approve", "Within current authority and reviewed proof", [])).ToArray(), Guid.NewGuid().ToString(), Guid.NewGuid());
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
        var person = new Person { FullName = "Fictional policy customer" }; db.Add(person); await db.SaveChangesAsync();
        var contact = new Contact { ClientId = cycle.ClientId, RelationshipId = cycle.RelationshipId, PersonId = person.Id,
            DeclaredFullName = person.FullName, NormalizedName = person.FullName.ToUpperInvariant(), Role = "director", Email = "quotation-customer@example.invalid",
            MarketingConsent = "{\"state\":\"not-asked\",\"email\":false,\"telephone\":false,\"source\":\"Fictional fixture\",\"recordedAt\":\"2026-09-16T12:00:00Z\"}" };
        db.Add(contact); await db.SaveChangesAsync();
        return (f, await db.Set<TemplateVersion>().Where(x => x.ProductId == cycle.ProductId && x.Kind == "quote-terms").Select(x => x.Id).SingleAsync(), cycle.CurrentRatingId!.Value, contact.Id);
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlQuoteTermsPrepareSignDeliverAcceptRetainsExactContractWithoutSignatureLoop(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await TermsFixture(db, password, product); var f = setup.Fixture;
            var terms = new QuoteTermsService(f.Factory, f.Clock); var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var prepared = await terms.PrepareAsync(f.Servicing, f.QuoteId, f.CycleId, setup.RatingId, setup.TemplateId, await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            var row = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == prepared.ResourceId);
            var ratingHash = await db.Set<UnderwritingCycle>().AsNoTracking().Where(x => x.Id == f.CycleId).Select(x => x.PricingInputHash).SingleAsync();
            Assert.Equal("quote-proof-review-required", (await Assert.ThrowsAsync<QuoteOperationException>(async () => await terms.SendAsync(f.Servicing, f.QuoteId, row.Id, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid()))).Code);
            await TermsProof(db, f, (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "signed-statement"));
            var reused = await terms.PrepareAsync(f.Servicing, f.QuoteId, f.CycleId, setup.RatingId, setup.TemplateId, await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal(row.Id, reused.ResourceId); Assert.Single(await db.Set<QuoteTermsVersion>().ToArrayAsync());
            var sendVersion = await TermsVersion(db, f); var key = Guid.NewGuid().ToString();
            var sent = await terms.SendAsync(f.Servicing, f.QuoteId, row.Id, [setup.ContactId], sendVersion, key, Guid.NewGuid());
            Assert.True((await terms.SendAsync(f.Servicing, f.QuoteId, row.Id, [setup.ContactId], sendVersion, key, Guid.NewGuid())).Replayed);
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var sharingAgency=await db.Set<Quote>().Where(x=>x.Id==f.QuoteId).Select(x=>x.AgencyId).SingleAsync();
            async Task<AgencySharingPage<AgencySharedOpenItem>> Shared(DateTimeOffset? at=null)=>await AgencySharingService.PreviewOpenItems(db,f.Underwriter,sharingAgency,new(At:at??f.Clock.GetUtcNow()));
            Assert.DoesNotContain((await Shared()).Items,x=>x.Id==row.Id);
            Assert.Equal("queued", delivery.State); Assert.Single(await db.Set<OutboxWork>().Where(x => x.Kind == QuoteTermsService.WorkKind).ToArrayAsync());
            var leases = new SqlJobLeases(f.Factory, f.Clock); var worker = new QuoteDeliveryWorker(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            var outcome = await worker.ExecuteProviderAsync(lease); Assert.True(await worker.ApplyAsync(lease, outcome)); Assert.False(await worker.ApplyAsync(lease, outcome));
            Assert.Equal("quotation-acceptance",Assert.Single((await Shared()).Items,x=>x.Id==row.Id).Kind);
            var expires=await db.Set<QuoteRatingResult>().Where(x=>x.Id==setup.RatingId).Select(x=>x.ExpiresAt).SingleAsync();
            Assert.DoesNotContain((await Shared(expires)).Items,x=>x.Id==row.Id);
            Assert.Equal("sent", await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.State).SingleAsync());
            var proofId = await TermsProof(db, f, (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "acceptance-proof"));
            var assessment = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
            var acceptance = new QuoteAcceptanceService(f.Factory, f.Clock);
            var input = new QuoteAcceptanceInput(f.CycleId, setup.RatingId, row.Id, row.TermsHash, (string)assessment["assuranceHash"], "Fictional policy customer", f.Clock.Current, "email", proofId);
            var acceptVersion = await TermsVersion(db, f); var acceptKey = Guid.NewGuid().ToString();
            var accepted = await acceptance.RecordAsync(f.Servicing, f.QuoteId, acceptVersion, input, acceptKey, Guid.NewGuid());
            Assert.DoesNotContain((await Shared()).Items,x=>x.Id==row.Id);
            Assert.True((await acceptance.RecordAsync(f.Servicing, f.QuoteId, acceptVersion, input, acceptKey, Guid.NewGuid())).Replayed);
            Assert.Equal("accepted", await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.State).SingleAsync());
            Assert.Equal(input.AssuranceHash, (await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"]);
            Assert.Equal(ratingHash, await db.Set<UnderwritingCycle>().AsNoTracking().Where(x => x.Id == f.CycleId).Select(x => x.PricingInputHash).SingleAsync());
            Assert.Equal(row.TermsJson, (await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == row.Id)).TermsJson);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteTermsVersion SET TermsJson=N'{{}}' WHERE Id={row.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteAcceptance SET AccepterLabel=N'Changed' WHERE Id={accepted.ResourceId}"));
        });
    }
}
