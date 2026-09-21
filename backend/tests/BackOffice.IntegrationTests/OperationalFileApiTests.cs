using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalFileApiTests
{
    [Fact]
    public async Task RealSqlFileApiStreamsBytesChecksHeadersCsrfScopeAndRestart()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CoverMGA_FileApi")); var root = Path.Combine(parent, owned);
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nFictional API document\n%%EOF\n");
        WebApplicationFactory<Program> Host(bool worker = false) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection", connection.ConnectionString).UseSetting("Cover:FileStoragePath", Path.Combine(root, "files"))
            .UseSetting("Cover:FileWorkerEnabled", worker.ToString()).UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:DataProtectionPath", Path.Combine(root, "keys")));
        try
        {
            Guid subjectId;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
                var agency = new Agency { Reference = "AG-FILE-API", LegalName = "Fictional file API" }; db.Add(agency); await db.SaveChangesAsync();
                var subject = new OperationalSubject { Kind = "agency", AgencyId = agency.Id, CreatedBy = user.Id }; db.Add(subject); await db.SaveChangesAsync(); subjectId = subject.Id;
            }
            Guid uploadId;
            using (var host = Host())
            using (var client = host.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/file-uploads/" + Guid.NewGuid())).StatusCode);
                var csrf = await Login(client, "agency-admin@cover.example", password);
                var path = $"/api/v1/records/{subjectId}/file-uploads?name=fictional.pdf";
                Assert.Equal(HttpStatusCode.Forbidden, (await Upload(client, path, null, "missing-csrf", bytes)).StatusCode);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(client, path.Replace("fictional.pdf", "escape%2Ffictional.pdf"), csrf, "path", bytes)).StatusCode);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(client, path, csrf, "spoof", Encoding.ASCII.GetBytes("Not a PDF"))).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, path + "&name=other.pdf", csrf, "duplicate-name", bytes)).StatusCode);
                using var created = await Upload(client, path, csrf, "upload", bytes); Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
                var body = await created.Content.ReadFromJsonAsync<JsonElement>(); uploadId = body.GetProperty("id").GetGuid();
                Assert.Equal("pending", body.GetProperty("state").GetString()); Assert.False(body.TryGetProperty("fileId", out _));
                Assert.Equal($"/api/v1/file-uploads/{uploadId}", created.Headers.Location!.ToString());
                Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/file-uploads/{uploadId}/content")).StatusCode);
                await using var scope = host.Services.CreateAsyncScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<FileFinalizationWorker>().Process(uploadId, default));
                using var downloaded = await client.GetAsync($"/api/v1/file-uploads/{uploadId}/content"); downloaded.EnsureSuccessStatusCode();
                Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync()); Assert.Equal("application/pdf", downloaded.Content.Headers.ContentType!.MediaType);
                Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
                Assert.Contains("fictional.pdf", downloaded.Content.Headers.ContentDisposition.ToString());
                Assert.True(downloaded.Headers.CacheControl!.NoStore); Assert.Equal("nosniff", Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
                using var servicing = host.CreateClient(); await Login(servicing, "servicing@cover.example", password);
                Assert.Equal(HttpStatusCode.NotFound, (await servicing.GetAsync($"/api/v1/file-uploads/{uploadId}/content")).StatusCode);
            }
            using (var restarted = Host(worker: true))
            using (var client = restarted.CreateClient())
            {
                var csrf = await Login(client, "agency-admin@cover.example", password);
                Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/file-uploads/{uploadId}/content"));
                var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/file-uploads/{uploadId}"); Assert.Equal("ready", status.GetProperty("state").GetString());
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), status.GetProperty("sha256").GetString());
                using var automatic = await Upload(client, $"/api/v1/records/{subjectId}/file-uploads?name=fictional.pdf", csrf, "hosted-worker", bytes);
                Assert.Equal(HttpStatusCode.Accepted, automatic.StatusCode);
                var automaticId = (await automatic.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                var ready = false;
                for (var poll = 0; poll < 60 && !ready; poll++)
                {
                    var current = await client.GetFromJsonAsync<JsonElement>($"/api/v1/file-uploads/{automaticId}"); ready = current.GetProperty("state").GetString() == "ready";
                    if (!ready) await Task.Delay(500);
                }
                Assert.True(ready, "Hosted file dispatcher did not finalize its durable work.");
                await using var db = new BackOfficeDbContext(options);
                var fileId = await db.Set<FileObject>().Where(x => x.WorkId == automaticId).Select(x => x.Id).SingleAsync();
                await File.WriteAllBytesAsync(Path.Combine(root, "files", "ready", fileId.ToString("N") + ".bin"), Encoding.ASCII.GetBytes("Tampered bytes"));
                using var rejected = await client.GetAsync($"/api/v1/file-uploads/{automaticId}/content"); Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
                Assert.DoesNotContain(root, await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal("quarantined", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/file-uploads/{automaticId}")).GetProperty("state").GetString());
                Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/file-uploads/{automaticId}/content")).StatusCode);
            }
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved) != owned) throw new InvalidOperationException("File cleanup target changed.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private static async Task<string> Login(HttpClient client, string email, string password)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) }; request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var login = await client.SendAsync(request); login.EnsureSuccessStatusCode();
        return (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string path, string? csrf, string key, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/pdf"); request.Headers.Add("Idempotency-Key", "file-api-acceptance-" + key);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }
}
