using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlQuoteReferralAnyDriverWarrantyNeedsAcknowledgementAndNeverWaivesAge(bool young)
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, proposal => {
                using var stream = typeof(QuoteCaptureShape).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
                using var catalogue = JsonDocument.Parse(stream);
                JsonNode Reference(string collection, int value)
                {
                    var row = catalogue.RootElement.GetProperty("collections").GetProperty(collection).EnumerateArray().Single(x => x.GetProperty("value").GetInt32() == value);
                    return JsonSerializer.SerializeToNode(new { collection, value, label = row.GetProperty("text").GetString(), version = QuoteCatalogueIdentity.Version })!;
                }
                var answers = proposal["risk"]!["responses"]!["answers"]!.AsArray();
                answers.Single(x => x!["questionId"]!.GetValue<string>() == "MTS-06-Q01")!["value"] = Reference("driverPlans", 2);
                answers.Add(new JsonObject { ["questionId"] = "MTS-06-Q02", ["kind"] = "count", ["value"] = 2 });
                foreach (var (question, collection, value) in new[] { ("MTS-06-Q03", "aadDriverMinAge", young ? 1 : 3), ("MTS-06-Q04", "aadDriverMaxAge", 1),
                    ("MTS-06-Q05", "aadMaxVehicleGrouping", 1), ("MTS-06-Q06", "aadMaxVehicleGvw", 1), ("MTS-06-Q07", "aadMaxMotorcycleCc", 1) })
                    answers.Add(new JsonObject { ["questionId"] = question, ["kind"] = "reference", ["value"] = Reference(collection, value) });
            });
            var service = new QuoteReferralService(f.Factory, f.Clock); var proof = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RuleCode == "any-driver-licence-years");
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            var warranty = JsonSerializer.SerializeToElement(new { code = "any-driver-minimum-licence", minimumYears = 2, wordingVersion = "1" });
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve", "Age does not establish licence experience", [])], Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            async Task Decide() => await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve-with-conditions", "Require explicit minimum licence warranty", [warranty])], Guid.NewGuid().ToString(), Guid.NewGuid());
            if (young) { Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(Decide)).Status); Assert.Empty(await db.Set<QuoteReferralDecision>().ToArrayAsync()); return; }
            await Decide();
            var condition = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.ReferralId == referral.Id);
            Assert.Contains("at least 2 complete years", condition.Wording); Assert.Equal("conditional", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            var required = (await proof.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.ConditionId == condition.Id);
            Assert.False(required.Satisfied);
            var file = await proof.UploadAsync(f.Servicing, f.QuoteId, await Version(), "signed-ack.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional signed acknowledgement of the explicit warranty"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var attached = await proof.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), file.ResourceId, required.Code, null, condition.Id, null, required.InputFingerprint, "Exact warranty acknowledgement", Guid.NewGuid().ToString(), Guid.NewGuid());
            var association = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await proof.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, association.Id, await Version(), association.RowVersion, "accepted", required.InputFingerprint, "Reviewed signed acknowledgement", Guid.NewGuid().ToString(), Guid.NewGuid());
            await service.ResolveAsync(f.Underwriter, f.QuoteId, referral.Id, f.CycleId, condition.Id, await Version(), condition.RowVersion, association.Id, "satisfied", "Acknowledgement accepted", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("approved", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
            var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId);
            using var kept = JsonDocument.Parse(revision.ProposalJson); Assert.Single(kept.RootElement.GetProperty("risk").GetProperty("drivers").EnumerateArray());
            var assessment = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
            Assert.Contains("photocard-both-sides", JsonSerializer.Serialize(assessment["blockers"]));
        });
    }
}
