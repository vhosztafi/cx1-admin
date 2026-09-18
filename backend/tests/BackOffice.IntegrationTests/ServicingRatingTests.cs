using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlServicingRatingTestsStorageRequiresOwnedImmutableInput(bool late)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            // Verify the latest additive upgrade against an already issued graph.
            // Current published templates cannot be downgraded to the older
            // pre-servicing template enum without deleting genuine seed data.
            var previous = db.Database.GetMigrations().SkipLast(1).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            var service = new ServicingDraftService(f.Factory, f.Clock);
            var listed = await service.ListAsync(f.Servicing, issued.TermId);
            var created = await service.CreateAsync(f.Servicing, issued.TermId, Convert.FromBase64String(listed.Etag.Trim('"')),
                new("adjustment", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional rating storage test"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var revision = JsonSerializer.Deserialize<JsonElement>(created.Body).GetProperty("revisionId").GetGuid();
            var original = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == setup.Input.CycleId);
            var source = JsonSerializer.Deserialize<JsonElement>(original.InputJson);
            await using (var seedTransaction = await db.Database.BeginTransactionAsync())
            {
                await ServicingRatingSeed.SeedAsync(db); await ServicingRatingSeed.SeedAsync(db);
                await seedTransaction.CommitAsync();
            }
            var servicingSetting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Scope == ServicingRatingSeed.Scope);
            Assert.Equal(15m, BackOffice.Application.Policies.ServicingRatingConfiguration.Parse(servicingSetting.Values)!.AdjustmentFee);
            var now = f.Clock.GetUtcNow(); var id = Guid.NewGuid(); var workId = Guid.NewGuid();
            var scenario = source.GetProperty("scenarioVersionId").GetGuid(); var runtime = source.GetProperty("runtimeVersionId").GetGuid();
            db.Add(new OutboxWork { Id = workId, Kind = "servicing-rating", SubjectRecordId = id, OperationKey = "servicing-rating/" + id.ToString("N"), ScenarioVersionId = scenario,
                Payload = JsonSerializer.Serialize(new { cycleId = id, draftId = created.ResourceId }), NextAttemptAt = now, CorrelationId = Guid.NewGuid(), CreatedBy = f.Servicing.UserId, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            var revisionHash = await db.Set<ServicingRevision>().Where(x => x.Id == revision).Select(x => x.ContentHash).SingleAsync();
            var json = JsonSerializer.Serialize(new { format = "servicing-rating-input-1", draftId = created.ResourceId, revisionId = revision, baseVersionId = issued.Id,
                productVersionId = original.ProductVersionId, agencyTermsVersionId = original.AgencyTermsVersionId, ratingRuleVersionId = original.RatingRuleVersionId,
                binderVersionId = original.BinderVersionId, authorityVersionId = original.AuthorityVersionId, runtimeVersionId = runtime, scenarioVersionId = scenario, servicingSettingVersionId = servicingSetting.Id, fee = 15m, requestedAt = now, requestedBy = f.Servicing.UserId,
                policyId = issued.PolicyId, baseTermId = issued.TermId, baseContentHash = Convert.ToHexStringLower(issued.ContentHash), revisionContentHash = Convert.ToHexStringLower(revisionHash) });
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
            async Task Insert(Guid draft, byte[] inputHash, bool missingSetting = false) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCycle (Id,DraftId,PolicyId,BaseTermId,BaseVersionId,RevisionId,ProductId,ProductVersionId,AgencyTermsVersionId,RatingRuleVersionId,BinderVersionId,AuthorityVersionId,RuntimeVersionId,ScenarioVersionId,ServicingSettingVersionId,Sequence,WorkId,InputHash,InputJson,RequestedBy,State,CreatedBy,CreatedAt,UpdatedAt) VALUES ({id},{draft},{issued.PolicyId},{issued.TermId},{issued.Id},{revision},{original.ProductId},{original.ProductVersionId},{original.AgencyTermsVersionId},{original.RatingRuleVersionId},{original.BinderVersionId},{original.AuthorityVersionId},{runtime},{scenario},{(missingSetting ? (Guid?)null : servicingSetting.Id)},1,{workId},{inputHash},{json},{f.Servicing.UserId},'rating-pending',{f.Servicing.UserId},{now},{now})");
            await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), hash));
            await Assert.ThrowsAsync<SqlException>(() => Insert(created.ResourceId, new byte[32]));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET ScenarioVersionId=NULL WHERE Id={workId}");
            await Assert.ThrowsAsync<SqlException>(() => Insert(created.ResourceId, hash));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET ScenarioVersionId={scenario} WHERE Id={workId}");
            await Assert.ThrowsAsync<SqlException>(() => Insert(created.ResourceId, hash, missingSetting: true));
            var originalJson = json; var originalHash = hash;
            foreach (var property in new[] { "fee", "requestedAt", "baseContentHash", "revisionContentHash", "policyId" })
            {
                var altered = System.Text.Json.Nodes.JsonNode.Parse(originalJson)!;
                altered[property] = property switch
                {
                    "fee" => System.Text.Json.Nodes.JsonValue.Create(16m),
                    "requestedAt" => System.Text.Json.Nodes.JsonValue.Create(now.AddSeconds(1)),
                    "policyId" => System.Text.Json.Nodes.JsonValue.Create(Guid.NewGuid()),
                    _ => System.Text.Json.Nodes.JsonValue.Create(new string('0', 64))
                };
                json = altered.ToJsonString(); hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
                await Assert.ThrowsAsync<SqlException>(() => Insert(created.ResourceId, hash));
            }
            json = originalJson; hash = originalHash;
            await Insert(created.ResourceId, hash);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET State='rated' WHERE Id={id}"));
            var cancellation = await service.CreateAsync(f.Servicing, issued.TermId,
                Convert.FromBase64String((await service.ListAsync(f.Servicing, issued.TermId)).Etag.Trim('"')),
                new("cancellation", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional foreign cycle pointer"), Guid.NewGuid().ToString(), Guid.NewGuid());
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET CurrentCycleId={id} WHERE Id={cancellation.ResourceId}"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET CurrentCycleId={id} WHERE Id={created.ResourceId}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET InputJson=N'{{}}' WHERE Id={id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET RevisionId={Guid.NewGuid()} WHERE Id={id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCycle WHERE Id={id}"));
            if (late)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET State='superseded',SupersededAt={now},SupersededReason=N'New revision required' WHERE Id={id}");
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET State='rating-pending',SupersededAt=NULL,SupersededReason=NULL WHERE Id={id}"));
            }
            // A late provider outcome remains immutable history even when its
            // cycle was superseded; it cannot become the current rated result.
            var attempt = new AdapterAttempt { WorkId = workId, AttemptNumber = 1, StartedAt = now, CreatedAt = now };
            var operation = new DemoProviderOperation { Kind = "servicing-rating", OperationKey = "servicing-rating/" + id.ToString("N"),
                RequestHash = hash, ScenarioVersionId = scenario, State = "succeeded", CompletedAt = now, CreatedAt = now, UpdatedAt = now };
            var output = JsonSerializer.Serialize(new { format = "servicing-rating-result-1", operationId = operation.Id, outcome = "rated", completedAt = now, expiresAt = now.AddDays(30),
                rating = new { baseAnnualPremium = 1200m, premium = -177.53m, tax = -21.30m, brokerCommission = -17.75m, fee = 15m, grossPayable = -183.83m, netDue = -166.08m } });
            operation.Result = output; db.AddRange(attempt, operation); await db.SaveChangesAsync();
            var result = Guid.NewGuid(); var resultHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(output));
            async Task InsertResult(Guid attemptId, byte[] outputHash, decimal premium = -177.53m) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingRatingResult (Id,CycleId,DraftId,RevisionId,WorkId,AttemptId,ProviderOperationId,RuleVersionId,InputHash,ResultHash,ResultJson,Outcome,CompletedAt,ExpiresAt,BaseAnnualPremium,Premium,Tax,Fee,BrokerCommission,GrossPayable,NetDue,CreatedAt) VALUES ({result},{id},{created.ResourceId},{revision},{workId},{attemptId},{operation.Id},{original.RatingRuleVersionId},{hash},{outputHash},{output},'rated',{now},{now.AddDays(30)},1200,{premium},-21.30,15,-17.75,{premium - 6.30m},{premium + 11.45m},{now})");
            await Assert.ThrowsAsync<SqlException>(() => InsertResult(Guid.NewGuid(), resultHash));
            await Assert.ThrowsAsync<SqlException>(() => InsertResult(attempt.Id, new byte[32]));
            await Assert.ThrowsAsync<SqlException>(() => InsertResult(attempt.Id, resultHash, -1m));
            await InsertResult(attempt.Id, resultHash);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingRatingResult SET Premium=-1 WHERE Id={result}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingRatingResult WHERE Id={result}"));
            if (late)
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET CurrentRatingId={result} WHERE Id={id}"));
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET State='rated',CurrentRatingId={result} WHERE Id={id}");
                var rated = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == id);
                Assert.Equal("rated", rated.State); Assert.Equal(result, rated.CurrentRatingId);
            }
            var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            Assert.Equal(issued.SnapshotJson, retained.SnapshotJson); Assert.Equal(issued.ContentHash, retained.ContentHash);
        });
    }
}
