using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteTermsWarrantyChangeRequiresNewSignatureDeliveryAndAcceptance()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password, tradingReferral: true); var f = setup.Fixture;
            var service = new QuoteTermsService(f.Factory, f.Clock); var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var first = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == setup.TermsId);
            var sent = await service.SendAsync(f.Servicing, f.QuoteId, first.Id, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var worker = new QuoteDeliveryWorker(f.Factory, f.Clock); var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var proof = await TermsProof(db, f, (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "acceptance-proof"));
            var assessment = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
            var input = new QuoteAcceptanceInput(f.CycleId, setup.RatingId, first.Id, first.TermsHash, (string)assessment["assuranceHash"], "Fictional actual customer", f.Clock.Current, "telephone", proof);
            await new QuoteAcceptanceService(f.Factory, f.Clock).RecordAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f), input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId);
            var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == (db.Set<UnderwritingCycle>().Where(c => c.Id == f.CycleId).Select(c => c.QuoteRevisionId).Single()));
            using var proposal = JsonDocument.Parse(revision.ProposalJson);
            var driverIds = proposal.RootElement.GetProperty("risk").GetProperty("drivers").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
            var warranty = JsonSerializer.SerializeToElement(new { code = "named-drivers-only", driverIds, wordingVersion = "1" });
            var referrals = new QuoteReferralService(f.Factory, f.Clock);
            await referrals.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await TermsVersion(db, f), [new(referral.Id, referral.RowVersion, "approve-with-conditions", "Require named drivers only", [warranty])], Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.False((await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId)).ContainsKey("acceptanceId"));
            var condition = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId);
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.ConditionId == condition.Id);
            var acknowledgement = await TermsProof(db, f, purpose);
            await referrals.ResolveAsync(f.Underwriter, f.QuoteId, referral.Id, f.CycleId, condition.Id, await TermsVersion(db, f), condition.RowVersion, acknowledgement,
                "satisfied", "Actual warranty acknowledgement reviewed", Guid.NewGuid().ToString(), Guid.NewGuid());
            var second = await service.PrepareAsync(f.Servicing, f.QuoteId, f.CycleId, setup.RatingId, first.TemplateVersionId, await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.NotEqual(first.Id, second.ResourceId);
            var current = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == second.ResourceId);
            Assert.NotEqual(first.TermsHash, current.TermsHash); Assert.Contains("named-drivers-only", current.TermsJson);
            Assert.Equal(first.TermsJson, await db.Set<QuoteTermsVersion>().Where(x => x.Id == first.Id).Select(x => x.TermsJson).SingleAsync());
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId); Assert.Null(cycle.CurrentAcceptanceId); Assert.Null(cycle.CurrentDeliveryId);
            Assert.False((await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "signed-statement").Satisfied);
            await Assert.ThrowsAsync<BackOffice.Infrastructure.Quotes.QuoteOperationException>(async () => await service.SendAsync(f.Servicing, f.QuoteId, current.Id, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid()));
            await Assert.ThrowsAsync<BackOffice.Infrastructure.Quotes.QuoteOperationException>(async () => await new QuoteAcceptanceService(f.Factory, f.Clock).RecordAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f), input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Single(await db.Set<QuoteAcceptance>().ToArrayAsync()); Assert.Single(await db.Set<QuoteTermsDelivery>().ToArrayAsync());
        });
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("runtime")]
    public async Task RealSqlQuoteTermsRejectsExpiredOrReconfiguredPricing(string change)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture;
            if (change == "expiry") f.Clock.Current += TimeSpan.FromDays(14);
            else
            {
                var original = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Scope == "underwriting-runtime");
                db.Add(new SettingVersion { Scope = original.Scope, Version = original.Version + 1, Values = original.Values, EffectiveFrom = f.Clock.Current }); await db.SaveChangesAsync();
            }
            await Assert.ThrowsAsync<QuoteOperationException>(async () => await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Empty(await db.Set<QuoteTermsDelivery>().ToArrayAsync()); Assert.Empty(await db.Set<QuoteAcceptance>().ToArrayAsync());
        });
    }

    [Fact]
    public async Task RealSqlQuoteAcceptanceRejectsUnsentQueuedFutureForeignAndUnreviewedProofThenDetectsWithdrawal()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture;
            var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == setup.TermsId);
            var service = new QuoteAcceptanceService(f.Factory, f.Clock); var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            async Task<QuoteAcceptanceInput> Input(Guid proof) => new(f.CycleId, setup.RatingId, terms.Id, terms.TermsHash,
                (string)(await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"], "Actual fictional customer", f.Clock.Current, "written", proof);
            async Task Reject(QuoteAcceptanceInput input) => await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.RecordAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f), input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            await Reject(await Input(Guid.NewGuid()));
            var sent = await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, terms.Id, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            await Reject(await Input(Guid.NewGuid()));
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!; var worker = new QuoteDeliveryWorker(f.Factory, f.Clock);
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "acceptance-proof");
            var proofId = await TermsProof(db, f, purpose, review: false);
            await Reject(await Input(proofId)); await Reject(await Input(Guid.NewGuid()));
            var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == proofId);
            await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, proofId, await TermsVersion(db, f), proof.RowVersion, "accepted", purpose.InputFingerprint, "Actual reviewed acceptance", Guid.NewGuid().ToString(), Guid.NewGuid());
            var input = await Input(proofId); await Reject(input with { AcceptedAt = f.Clock.Current.AddSeconds(1) }); await Reject(input with { AcceptedAt = f.Clock.Current.AddSeconds(-1) });
            await Reject(input with { AssuranceHash = new string('b', 64) }); await Reject(input with { TermsHash = new string('b', 64) });
            var version = await TermsVersion(db, f); var key = Guid.NewGuid().ToString(); var accepted = await service.RecordAsync(f.Servicing, f.QuoteId, version, input, key, Guid.NewGuid());
            var after = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId); Assert.Equal(accepted.ResourceId, after["acceptanceId"]);
            proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == proofId);
            await evidence.WithdrawAsync(f.Underwriter, f.QuoteId, f.CycleId, proofId, await TermsVersion(db, f), proof.RowVersion, "Customer evidence withdrawn", Guid.NewGuid().ToString(), Guid.NewGuid());
            var stale = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
            Assert.False(stale.ContainsKey("acceptanceId")); Assert.Contains(((IEnumerable<object>)stale["blockers"]).OfType<UnderwritingReadBlocker>(), x => x.Code == "quote-acceptance-stale");
            await Reject(await Input(proofId)); Assert.Single(await db.Set<QuoteAcceptance>().ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Servicing.UserId}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.RecordAsync(f.Servicing, f.QuoteId, version, input, key, Guid.NewGuid()));
        });
    }
}
