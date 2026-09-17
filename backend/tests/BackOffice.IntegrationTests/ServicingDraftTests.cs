using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlServicingDraftTestsCommandsFenceOldEditorsAndReauthorizeReceiptReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var service = new ServicingDraftService(f.Factory, f.Clock);
            static byte[] Version(string etag) => Convert.FromBase64String(etag.Trim('"'));
            static JsonElement Body(string body) => JsonSerializer.Deserialize<JsonElement>(body);
            static string Key() => Guid.NewGuid().ToString();
            var listed = await service.ListAsync(f.Servicing, issued.TermId);
            var created = await service.CreateAsync(f.Servicing, issued.TermId, Version(listed.Etag),
                new("adjustment", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional draft for lease tests"), Key(), Guid.NewGuid());
            var id = created.ResourceId;
            var acquired = await service.LeaseAsync(f.Servicing, id, Version(created.Etag!), "acquire", null, null, Key(), Guid.NewGuid());
            var firstFence = Body(acquired.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var proposal = Body(acquired.Body).GetProperty("proposal").GetRawText();
            var saveVersion = Version(acquired.Etag!); var saveKey = Key();
            var foreignProposal = System.Text.Json.Nodes.JsonNode.Parse(proposal)!;
            foreignProposal["changes"] = JsonSerializer.SerializeToNode(new[] { new { changeId = Guid.NewGuid(), riskItemId = Guid.NewGuid(), kind = "driver", operation = "update", payload = new { } } });
            Assert.Throws<BackOffice.Application.Quotes.QuoteInputException>(() => BackOffice.Application.Policies.ServicingProposalInput.Parse(proposal, Guid.NewGuid()));
            await Assert.ThrowsAsync<BackOffice.Application.Quotes.QuoteInputException>(() => service.SaveAsync(f.Servicing, id, saveVersion, firstFence, foreignProposal.ToJsonString(), Key(), Guid.NewGuid()));
            var saved = await service.SaveAsync(f.Servicing, id, saveVersion, firstFence, proposal, saveKey, Guid.NewGuid());
            Assert.Equal(2, await db.Set<ServicingRevision>().CountAsync(x => x.DraftId == id));
            Assert.True((await service.SaveAsync(f.Servicing, id, saveVersion, firstFence, proposal, saveKey, Guid.NewGuid())).Replayed);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(f.Servicing, id, saveVersion, firstFence, proposal, Key(), Guid.NewGuid()))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.LeaseAsync(f.Underwriter, id, Version(saved.Etag!), "acquire", null, null, Key(), Guid.NewGuid()))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.LeaseAsync(f.Servicing, id, Version(saved.Etag!), "takeover", null, "Fictional reason for takeover", Key(), Guid.NewGuid()))).Status);
            var takeover = await service.LeaseAsync(f.Underwriter, id, Version(saved.Etag!), "takeover", null, "Fictional authorised takeover", Key(), Guid.NewGuid());
            var secondFence = Body(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid(); Assert.NotEqual(firstFence, secondFence);
            foreach (var action in new[] { "renew", "release" })
                Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.LeaseAsync(f.Servicing, id, Version(takeover.Etag!), action, firstFence, null, Key(), Guid.NewGuid()))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(f.Servicing, id, Version(takeover.Etag!), firstFence, proposal, Key(), Guid.NewGuid()))).Status);
            f.Clock.Current = f.Clock.Current.AddMinutes(5);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.LeaseAsync(f.Underwriter, id, Version(takeover.Etag!), "renew", secondFence, null, Key(), Guid.NewGuid()))).Status);
            var reacquired = await service.LeaseAsync(f.Servicing, id, Version(takeover.Etag!), "acquire", null, null, Key(), Guid.NewGuid());
            var thirdFence = Body(reacquired.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid(); Assert.NotEqual(secondFence, thirdFence);
            var abandoned = await service.AbandonAsync(f.Servicing, id, Version(reacquired.Etag!), thirdFence, "Fictional draft no longer needed", Key(), Guid.NewGuid());
            Assert.Equal("abandoned", Body(abandoned.Body).GetProperty("state").GetString());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.LeaseAsync(f.Servicing, id, Version(abandoned.Etag!), "acquire", null, null, Key(), Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(f.Servicing, id, saveVersion, firstFence, proposal, saveKey, Guid.NewGuid()))).Status);
            var unchanged = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(); Assert.Equal(issued.ContentHash, unchanged.ContentHash); Assert.Equal(issued.SnapshotJson, unchanged.SnapshotJson);
        });
    }

    [Fact]
    public async Task RealSqlServicingDraftTestsStorageProtectsBaseRevisionAndActiveUniqueness()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            // Apply the additive upgrade to an already issued policy, rather than
            // only testing creation of an empty database at the latest version.
            var previous = db.Database.GetMigrations().TakeWhile(x => !x.EndsWith("_ServicingDraftStorage", StringComparison.Ordinal)).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            var actor = f.Servicing.UserId; var now = f.Clock.GetUtcNow();
            var foreignQuote = await db.Set<Quote>().AsNoTracking().FirstAsync(x => x.Id != f.QuoteId);
            var foreignProductVersion = await db.Set<ProductVersion>().AsNoTracking().FirstAsync(x => x.ProductId == foreignQuote.ProductId);
            var foreignPolicy = new Policy { SourceQuoteId = foreignQuote.Id, AgencyId = foreignQuote.AgencyId, ClientId = foreignQuote.ClientId,
                RelationshipId = foreignQuote.RelationshipId, ProductId = foreignQuote.ProductId, CreatedBy = actor };
            db.Add(foreignPolicy); await db.SaveChangesAsync();
            var foreignTerm = new PolicyTerm { PolicyId = foreignPolicy.Id, ProductId = foreignPolicy.ProductId, ProductVersionId = foreignProductVersion.Id,
                Number = 1, StartsAt = now, EndsAt = now.AddYears(1), CreatedBy = actor };
            db.Add(foreignTerm); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingDraft (Id,PolicyId,BaseTermId,BaseVersionId,Kind,State,CreatedBy,CreatedAt,UpdatedAt) VALUES ({Guid.NewGuid()},{foreignPolicy.Id},{foreignTerm.Id},{issued.Id},'adjustment','draft',{actor},{now},{now})"));
            async Task Insert(Guid id, string kind, Guid baseId) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingDraft (Id,PolicyId,BaseTermId,BaseVersionId,Kind,State,CreatedBy,CreatedAt,UpdatedAt) VALUES ({id},{issued.PolicyId},{issued.TermId},{baseId},{kind},'draft',{actor},{now},{now})");
            var draft = Guid.NewGuid(); await Insert(draft, "adjustment", issued.Id);
            await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), "adjustment", issued.Id));
            await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), "renewal", Guid.NewGuid()));
            var cancellation = Guid.NewGuid(); await Insert(cancellation, "cancellation", issued.Id);
            var renewal = Guid.NewGuid(); await Insert(renewal, "renewal", issued.Id);
            await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), "renewal", issued.Id));
            var revision = Guid.NewGuid();
            var json = "{\"schemaVersion\":\"1.0\",\"baseVersionId\":\"" + issued.Id + "\",\"changes\":[]}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingRevision (Id,DraftId,Sequence,SchemaVersion,ProposalJson,ContentHash,CreatedBy,CreatedAt) VALUES ({Guid.NewGuid()},{draft},1,'1.0',{json},{SHA256.HashData([1])},{actor},{now})"));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingRevision (Id,DraftId,Sequence,SchemaVersion,ProposalJson,ContentHash,CreatedBy,CreatedAt) VALUES ({revision},{draft},1,'1.0',{json},{hash},{actor},{now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET CurrentRevisionId={revision} WHERE Id={draft}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET CurrentRevisionId={revision} WHERE Id={cancellation}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingRevision SET ProposalJson=N'{{}}' WHERE Id={revision}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingRevision WHERE Id={revision}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET Kind='renewal' WHERE Id={draft}"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET State='abandoned' WHERE Id={draft}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingRevision (Id,DraftId,Sequence,SchemaVersion,ProposalJson,ContentHash,CreatedBy,CreatedAt) VALUES ({Guid.NewGuid()},{draft},2,'1.0',{json},{hash},{actor},{now})"));
            async Task<bool> Race()
            {
                await using var contender = f.Factory.CreateDbContext();
                try
                {
                    await contender.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingDraft (Id,PolicyId,BaseTermId,BaseVersionId,Kind,State,CreatedBy,CreatedAt,UpdatedAt) VALUES ({Guid.NewGuid()},{issued.PolicyId},{issued.TermId},{issued.Id},'adjustment','draft',{actor},{now},{now})");
                    return true;
                }
                catch (SqlException error) when (error.Number is 2601 or 2627) { return false; }
            }
            Assert.Single(await Task.WhenAll(Race(), Race()), x => x);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET State='draft' WHERE Id={draft}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyVersion SET SnapshotJson=N'{{}}' WHERE Id={issued.Id}"));
            var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            Assert.Equal(issued.SnapshotJson, retained.SnapshotJson); Assert.Equal(issued.ContentHash, retained.ContentHash);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
