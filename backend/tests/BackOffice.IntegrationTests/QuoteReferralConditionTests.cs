using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlQuoteReferralConditionsRequireExactReviewedProofAndWithdrawalRestoresBlocker(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, proposal => {
                proposal["risk"]!["business"]!["startedOn"] = "2025-01-01";
                if (product == "motor-trade-combined")
                    foreach (var section in proposal["cover"]!["requestedSections"]!.AsArray())
                        if (section!["code"]!.GetValue<string>() == "stock-custody") section["limit"] = "90000.00";
            }, product);
            var commands = new QuoteReferralService(f.Factory, f.Clock); var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var reads = new QuoteUnderwritingReadModel(f.Factory, f.Clock);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RuleCode == "UW-22");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await commands.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve", "Proof is not yet reviewed", [])], Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == db.Set<Quote>().Where(q => q.Id == f.QuoteId).Select(q => q.CurrentRevisionId).Single());
            using var proposal = JsonDocument.Parse(revision.ProposalJson);
            var warranty = product == "motor-trade-combined" ? JsonSerializer.SerializeToElement(new { code = "overnight-security", premisesId = proposal.RootElement.GetProperty("risk").GetProperty("premises")[0].GetProperty("id").GetGuid(), wordingVersion = "1" })
                : JsonSerializer.SerializeToElement(new { code = "named-drivers-only", driverIds = new[] { proposal.RootElement.GetProperty("risk").GetProperty("drivers")[0].GetProperty("id").GetGuid() }, wordingVersion = "1" });
            await commands.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve-with-conditions", "Review history and apply explicit warranty", [JsonSerializer.SerializeToElement(new { code = "provide-trading-history" }), warranty])], Guid.NewGuid().ToString(), Guid.NewGuid());
            var offered = JsonSerializer.SerializeToElement((await reads.AssessmentAsync(f.Servicing, f.QuoteId))["appliedEndorsements"]);
            Assert.Equal(product == "motor-trade-combined" ? "W-07" : "named-drivers-only", Assert.Single(offered.EnumerateArray()).GetProperty("code").GetString());
            var priceBefore = await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId);
            var file = await evidence.UploadAsync(f.Servicing, f.QuoteId, await Version(), "acknowledgement.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional documentary proof and explicit warranty acknowledgement"), Guid.NewGuid().ToString(), Guid.NewGuid());
            Guid associationToWithdraw = Guid.Empty;
            foreach (var condition in await db.Set<QuoteCondition>().AsNoTracking().Where(x => x.CycleId == f.CycleId).OrderBy(x => x.Sequence).ToArrayAsync())
            {
                var requirement = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.ConditionId == condition.Id);
                var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), file.ResourceId, requirement.Code, requirement.RiskItemId, condition.Id, null, requirement.InputFingerprint, "Supplied exact-purpose proof", Guid.NewGuid().ToString(), Guid.NewGuid());
                associationToWithdraw = attached.ResourceId;
                var association = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
                Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await commands.ResolveAsync(f.Underwriter, f.QuoteId, referral.Id, f.CycleId, condition.Id,
                    await Version(), condition.RowVersion, association.Id, "satisfied", "Screening is not a review", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
                await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, association.Id, await Version(), association.RowVersion, "accepted", requirement.InputFingerprint, "Independently reviewed content", Guid.NewGuid().ToString(), Guid.NewGuid());
                await commands.ResolveAsync(f.Underwriter, f.QuoteId, referral.Id, f.CycleId, condition.Id, await Version(), condition.RowVersion, association.Id, "satisfied", "Exact reviewed proof satisfies this condition", Guid.NewGuid().ToString(), Guid.NewGuid());
            }
            Assert.Equal("approved", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            var completed = (await reads.AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"];
            var withdrawal = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == associationToWithdraw);
            await evidence.WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, withdrawal.Id, await Version(), withdrawal.RowVersion, "Correct the acknowledged document", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("conditional", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            Assert.NotEqual(completed, (await reads.AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"]);
            Assert.Equal(priceBefore.ResultJson, (await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == priceBefore.Id)).ResultJson);
            Assert.Equal(2, await db.Set<QuoteConditionResolution>().CountAsync(x => x.CycleId == f.CycleId));
        });
    }
}
