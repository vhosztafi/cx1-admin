using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalTaskApiTests
{
    [Fact]
    public async Task RealSqlTaskApiChecksIdentityCsrfVersionsAndConcurrentChangedKey()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        try
        {
            Guid agencyId;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
                var agency = new Agency { Reference = "AG-TASK-API", LegalName = "Fictional task API" }; db.Add(agency); await db.SaveChangesAsync(); agencyId = agency.Id;
            }
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Cover:SqlConnection", connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "task-test-keys", owned))));
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/tasks/" + Guid.NewGuid())).StatusCode);
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = await Send(client, csrf, HttpMethod.Post, "/api/v1/auth/login", new { email = "agency-admin@cover.example", password })) login.EnsureSuccessStatusCode();
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var subject = await Send(client, csrf, HttpMethod.Post, "/api/v1/operational-subjects", new { kind = "agency", parentId = agencyId });
            Assert.Equal(HttpStatusCode.Created, subject.StatusCode); var subjectId = (await subject.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var assignees = await client.GetFromJsonAsync<JsonElement>($"/api/v1/task-assignees?subjectRecordId={subjectId}&kind=user");
            Assert.NotEmpty(assignees.GetProperty("items").EnumerateArray());
            Assert.All(assignees.GetProperty("items").EnumerateArray(), item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("label").GetString())));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/task-assignees?subjectRecordId={Guid.NewGuid()}&kind=user")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/task-assignees?subjectRecordId={subjectId}&subjectRecordId={subjectId}&kind=user")).StatusCode);
            object Body(string title) => new { subjectRecordId = subjectId, typeCode = "complaint", title, priority = "normal", assignment = new { kind = "unassigned" } };
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, null, HttpMethod.Post, "/api/v1/tasks", Body("Missing CSRF"))).StatusCode);
            var key = Guid.NewGuid().ToString("N");
            var competing = await Task.WhenAll(Send(client, csrf, HttpMethod.Post, "/api/v1/tasks", Body("First"), key), Send(client, csrf, HttpMethod.Post, "/api/v1/tasks", Body("Second"), key));
            var created = Assert.Single(competing, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(competing, x => x.StatusCode == HttpStatusCode.Conflict);
            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Matches("^TSK-[0-9]{7,}$", createdBody.GetProperty("reference").GetString()!);
            Assert.Equal("AG-TASK-API", createdBody.GetProperty("subject").GetProperty("label").GetString());
            Assert.Equal($"/agents/{agencyId}", createdBody.GetProperty("subject").GetProperty("href").GetString());
            Assert.Equal("Unassigned", createdBody.GetProperty("assignmentLabel").GetString());
            Assert.False(string.IsNullOrWhiteSpace(createdBody.GetProperty("createdByLabel").GetString()));
            var taskId = createdBody.GetProperty("id").GetGuid(); var etag = created.Headers.ETag!.ToString();
            var path = $"/api/v1/tasks/{taskId}";
            Assert.Equal((HttpStatusCode)428, (await Send(client, csrf, HttpMethod.Post, path + "/transition", new { state = "completed", reason = "Reviewed" })).StatusCode);
            using var completed = await Send(client, csrf, HttpMethod.Post, path + "/transition", new { state = "completed", reason = "Reviewed" }, etag: etag);
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            using var stale = await Send(client, csrf, HttpMethod.Post, path + "/transition", new { state = "open", reason = "Revisit" }, etag: etag);
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            Assert.Equal("completed", (await client.GetFromJsonAsync<JsonElement>(path)).GetProperty("state").GetString());
            using var second = await Send(client, csrf, HttpMethod.Post, "/api/v1/tasks", Body("Another task"));
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?pageSize=1");
            Assert.Equal(2, list.GetProperty("totalCount").GetInt32()); Assert.Equal(1, list.GetProperty("items").GetArrayLength());
            Assert.False(string.IsNullOrEmpty(list.GetProperty("items")[0].GetProperty("etag").GetString()));
            var cursor = list.GetProperty("nextCursor").GetString()!;
            var next = await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?pageSize=1&cursor=" + Uri.EscapeDataString(cursor));
            Assert.NotEqual(list.GetProperty("items")[0].GetProperty("id").GetGuid(), next.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?view=created-by-me")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?view=my-open")).GetProperty("totalCount").GetInt32());
            using var servicing = factory.CreateClient();
            var servicingCsrf = (await servicing.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = await Send(servicing, servicingCsrf, HttpMethod.Post, "/api/v1/auth/login", new { email = "servicing@cover.example", password })) login.EnsureSuccessStatusCode();
            Assert.Equal(0, (await servicing.GetFromJsonAsync<JsonElement>("/api/v1/tasks")).GetProperty("totalCount").GetInt32());
            var hiddenSummary = await servicing.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary");
            Assert.Equal(0, hiddenSummary.GetProperty("open").GetInt32());
            Assert.Equal(0, hiddenSummary.GetProperty("completedSevenDays").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await servicing.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await servicing.GetAsync($"/api/v1/task-assignees?subjectRecordId={subjectId}&kind=user")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await servicing.GetAsync("/api/v1/tasks?pageSize=1&cursor=" + Uri.EscapeDataString(cursor))).StatusCode);
            using var comment = await Send(client, csrf, HttpMethod.Post, path + "/comments", new { body = "Additional observation" }, etag: completed.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
            Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>(path + "/comments")).GetProperty("totalCount").GetInt32());
            Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>(path + "/events")).GetProperty("totalCount").GetInt32());
            var summary = await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary");
            Assert.Equal(1, summary.GetProperty("open").GetInt32());
            Assert.Equal(1, summary.GetProperty("completedSevenDays").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await servicing.GetAsync(path + "/comments")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/tasks?pageSize=1&cursor=" + Uri.EscapeDataString(cursor))).StatusCode);
            foreach (var response in competing) response.Dispose();
            await using var reload = new BackOfficeDbContext(options);
            Assert.Equal(2, await reload.Set<OperationalTask>().CountAsync()); Assert.Equal(4, await reload.Set<OperationalTaskEvent>().CountAsync());
            using var underwriter = factory.CreateClient();
            var uwCsrf = (await underwriter.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = await Send(underwriter, uwCsrf, HttpMethod.Post, "/api/v1/auth/login", new { email = "underwriter@cover.example", password })) login.EnsureSuccessStatusCode();
            uwCsrf = (await underwriter.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var authoredElsewhere = await Send(underwriter, uwCsrf, HttpMethod.Post, "/api/v1/tasks", Body("Another author"));
            Assert.Equal(HttpStatusCode.Created, authoredElsewhere.StatusCode);
            Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks")).GetProperty("totalCount").GetInt32());
            Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?view=created-by-me")).GetProperty("totalCount").GetInt32());
            var underwriterId = await reload.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var assigned = await Send(client, csrf, HttpMethod.Put, $"/api/v1/tasks/{secondId}", new { typeCode = "complaint", title = "Assigned task", priority = "normal", assignment = new { kind = "user", ownerId = underwriterId } }, etag: second.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
            var mine = await underwriter.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary");
            Assert.True(mine.TryGetProperty("mine",out var personal), "Task summary must distinguish assigned-to-me measures from all accessible work.");
            Assert.Equal(1,personal.GetProperty("open").GetInt32());
            Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary")).GetProperty("mine").GetProperty("open").GetInt32());
            var rosterPage = await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks?pageSize=1");
            var newTeam = new Team { Name = "Fictional reassigned team" }; reload.Add(newTeam); await reload.SaveChangesAsync();
            await reload.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET TeamId={newTeam.Id} WHERE Id={underwriterId}");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/tasks?pageSize=1&cursor=" + Uri.EscapeDataString(rosterPage.GetProperty("nextCursor").GetString()!))).StatusCode);
            // Historical fixture: a recent comment must not refresh an old completion.
            var oldCompletion = new OperationalTask { SubjectId = subjectId, Reference = "TSK-HISTORICAL", TypeCode = "complaint", Title = "Historical completed task", State = "completed", CompletionReason = "Historical fixture review", EventSequence = 2, CreatedBy = underwriterId, CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), UpdatedAt = DateTimeOffset.UtcNow };
            reload.Add(oldCompletion);
            reload.Add(new OperationalTaskEvent { TaskId = oldCompletion.Id, Sequence = 1, Kind = "task.transitioned", ActorLabel = "Fixture", CreatedBy = underwriterId, CreatedAt = DateTimeOffset.UtcNow.AddDays(-9), SnapshotJson = "{\"state\":\"completed\"}" });
            reload.Add(new OperationalTaskEvent { TaskId = oldCompletion.Id, Sequence = 2, Kind = "task.comment-added", ActorLabel = "Fixture", CreatedBy = underwriterId, CreatedAt = DateTimeOffset.UtcNow, SnapshotJson = "{\"state\":\"completed\"}" });
            reload.Add(new OperationalTaskComment { TaskId = oldCompletion.Id, AuthorLabel = "Fixture", CreatedBy = underwriterId, Body = "Recent observation on older work", CreatedAt = DateTimeOffset.UtcNow });
            await reload.SaveChangesAsync();
            Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary")).GetProperty("completedSevenDays").GetInt32());
            var teamChoices = await client.GetFromJsonAsync<JsonElement>($"/api/v1/task-assignees?subjectRecordId={subjectId}&kind=team&q=Fictional%20reassigned");
            Assert.Equal(newTeam.Id, Assert.Single(teamChoices.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            await reload.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={underwriterId}");
            var currentChoices = await client.GetFromJsonAsync<JsonElement>($"/api/v1/task-assignees?subjectRecordId={subjectId}&kind=user");
            Assert.DoesNotContain(currentChoices.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == underwriterId);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/task-assignees?subjectRecordId={subjectId}&kind=team&q=Fictional%20reassigned")).GetProperty("items").EnumerateArray());
            var account=await client.GetFromJsonAsync<JsonElement>("/api/v1/account");var actorId=account.GetProperty("id").GetGuid();
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).ToString("yyyy-MM-dd");
            using var personalTask=await Send(client,csrf,HttpMethod.Post,"/api/v1/tasks",new{subjectRecordId=subjectId,typeCode="complaint",title="Fictional personal summary",priority="normal",assignment=new{kind="user",ownerId=actorId},dueOn=today});personalTask.EnsureSuccessStatusCode();
            var personalId=(await personalTask.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var waiting=await Send(client,csrf,HttpMethod.Post,$"/api/v1/tasks/{personalId}/transition",new{state="awaiting-information",reason="Awaiting fictional agency response"},etag:personalTask.Headers.ETag!.ToString());waiting.EnsureSuccessStatusCode();
            var personalSummary=(await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary")).GetProperty("mine");
            Assert.Equal(1,personalSummary.GetProperty("open").GetInt32());Assert.Equal(1,personalSummary.GetProperty("dueToday").GetInt32());Assert.Equal(1,personalSummary.GetProperty("awaitingOthers").GetInt32());
            using var personalCompleted=await Send(client,csrf,HttpMethod.Post,$"/api/v1/tasks/{personalId}/transition",new{state="completed",reason="Fictional follow-up completed"},etag:waiting.Headers.ETag!.ToString());personalCompleted.EnsureSuccessStatusCode();
            var teamSummary=await client.GetFromJsonAsync<JsonElement>("/api/v1/tasks/summary");Assert.Equal(0,teamSummary.GetProperty("mine").GetProperty("open").GetInt32());Assert.Equal(1,teamSummary.GetProperty("teamCompletedSevenDays").GetInt32());
            var relationshipId = await reload.Set<ClientAgencyRelationship>().Where(x => x.State == "active").Select(x => x.Id).FirstAsync();
            var relationshipKey = Guid.NewGuid().ToString("N");
            using var related = await Send(client, csrf, HttpMethod.Post, "/api/v1/operational-subjects", new { kind = "relationship", parentId = relationshipId }, relationshipKey);
            Assert.Equal(HttpStatusCode.Created, related.StatusCode);
            await reload.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={relationshipId}");
            using var revokedReplay = await Send(client, csrf, HttpMethod.Post, "/api/v1/operational-subjects", new { kind = "relationship", parentId = relationshipId }, relationshipKey);
            Assert.Equal(HttpStatusCode.NotFound, revokedReplay.StatusCode);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string? csrf, HttpMethod method, string path, object body, string? key = null, string? etag = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        if (etag is not null) request.Headers.Add("If-Match", etag);
        return client.SendAsync(request);
    }
}
