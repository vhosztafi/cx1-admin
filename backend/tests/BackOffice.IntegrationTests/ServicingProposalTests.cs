using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlServicingProposalTestsTypedEditsRetainIssuedBytesAndRegistrations()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var registrations = await db.Set<PolicyRegistration>().AsNoTracking().OrderBy(x => x.RiskItemId).ToArrayAsync();
            var service = new ServicingDraftService(f.Factory, f.Clock);
            static byte[] Version(string etag) => Convert.FromBase64String(etag.Trim('"'));
            static string Key() => Guid.NewGuid().ToString();
            var listed = await service.ListAsync(f.Servicing, issued.TermId);
            var created = await service.CreateAsync(f.Servicing, issued.TermId, Version(listed.Etag),
                new("adjustment", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional typed servicing request"), Key(), Guid.NewGuid());
            var acquired = await service.LeaseAsync(f.Servicing, created.ResourceId, Version(created.Etag!), "acquire", null, null, Key(), Guid.NewGuid());
            var body = JsonNode.Parse(acquired.Body)!; var fence = Guid.Parse(body["lease"]!["leaseToken"]!.GetValue<string>());
            var proposal = body["proposal"]!.DeepClone(); var snapshot = JsonNode.Parse(issued.SnapshotJson)!;
            var driverId = snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<string>();
            var invalid = proposal.DeepClone();
            // Duplicate effective changes must fail atomically, without advancing
            // the draft ETag or appending either change independently.
            invalid["changes"] = JsonSerializer.SerializeToNode(new[] {
                new { changeId = Guid.NewGuid(), riskItemId = driverId, kind = "driver", operation = "update", payload = new { fullName = "First proposal" } },
                new { changeId = Guid.NewGuid(), riskItemId = driverId, kind = "driver", operation = "update", payload = new { fullName = "Conflicting proposal" } }
            });
            await Assert.ThrowsAsync<QuoteValidationException>(() => service.SaveAsync(f.Servicing, created.ResourceId, Version(acquired.Etag!), fence, invalid.ToJsonString(), Key(), Guid.NewGuid()));
            Assert.Equal(acquired.Etag, (await service.ReadAsync(f.Servicing, created.ResourceId)).Etag);
            var addedId = Guid.NewGuid();
            proposal["changes"] = JsonSerializer.SerializeToNode(new object[] {
                new { changeId = Guid.NewGuid(), riskItemId = driverId, kind = "driver", operation = "update", payloadMode = "replace", payload = new { fullName = "Fictional updated driver" } },
                new { changeId = Guid.NewGuid(), riskItemId = addedId, kind = "vehicle", operation = "add", payload = new { } }
            });
            var saved = await service.SaveAsync(f.Servicing, created.ResourceId, Version(acquired.Etag!), fence, proposal.ToJsonString(), Key(), Guid.NewGuid());
            var reloaded = await service.ReadAsync(f.Servicing, created.ResourceId);
            Assert.Equal(saved.Etag, reloaded.Etag);
            var savedProposal = JsonNode.Parse(reloaded.Body)!["proposal"]!;
            Assert.Equal(addedId.ToString(), savedProposal["changes"]![1]!["riskItemId"]!.GetValue<string>());
            Assert.Equal("Fictional updated driver", savedProposal["changes"]![0]!["payload"]!["fullName"]!.GetValue<string>());
            var editor = await service.ReadEditorAsync(f.Servicing, created.ResourceId);
            Assert.Equal(saved.Etag, editor.Etag);
            var assessment = JsonNode.Parse(editor.Body)!["assessment"]!;
            Assert.Equal("Fictional updated driver", assessment["proposed"]!["risk"]!["drivers"]![0]!["fullName"]!.GetValue<string>());
            Assert.Null(assessment["proposed"]!["risk"]!["drivers"]![0]!["dateOfBirth"]);
            Assert.NotNull(assessment["base"]!["risk"]!["drivers"]![0]!["dateOfBirth"]);
            Assert.Equal("replace", savedProposal["changes"]![0]!["payloadMode"]!.GetValue<string>());
            Assert.Contains(assessment["readinessIssues"]!.AsArray(), issue => issue!["path"]!.GetValue<string>() == "/risk/vehicles/1/registration");
            Assert.Equal(snapshot["risk"]!["drivers"]![0]!["fullName"]!.GetValue<string>(), assessment["base"]!["risk"]!["drivers"]![0]!["fullName"]!.GetValue<string>());
            Assert.Equal(2, await db.Set<ServicingRevision>().CountAsync(x => x.DraftId == created.ResourceId));
            var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            Assert.Equal(issued.SnapshotJson, retained.SnapshotJson); Assert.Equal(issued.ContentHash, retained.ContentHash);
            var currentRegistrations = await db.Set<PolicyRegistration>().AsNoTracking().OrderBy(x => x.RiskItemId).ToArrayAsync();
            Assert.Equal(JsonSerializer.Serialize(registrations), JsonSerializer.Serialize(currentRegistrations));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadEditorAsync(f.Servicing, created.ResourceId))).Status);
        });
    }
}
