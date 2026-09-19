using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyPolicyHistoryHttp(BackOfficeDbContext db, DecisionFixture f, string password, Policy policy, PolicyVersion version, Guid termsId, Guid foreignVersion)
    {
        using var host = ServicingRatingApiHost(db, f.Clock, false);
        using var client = host.CreateClient();
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
        {
            login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode();
        }
        csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        async Task<JsonElement> ReadJson(HttpResponseMessage response) => JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var root = $"/api/v1/policies/{policy.Id:D}";
        using var read = await client.GetAsync(root + "/history");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl!.NoStore);
        var etag = read.Headers.ETag!.ToString();
        async Task Capture(string schema, HttpResponseMessage response)
        {
            if (Environment.GetEnvironmentVariable("COVER_POLICY_HISTORY_RESPONSES") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{policy.Id:D}-{schema}.json"), JsonSerializer.Serialize(new { schema, data = await ReadJson(response) }));
        }
        await Capture("PolicyHistoryView", read);
        using var snapshot = JsonDocument.Parse(version.SnapshotJson);
        foreach (var kind in new[] { "drivers", "vehicles" })
        {
            var item = snapshot.RootElement.GetProperty("risk").GetProperty(kind).EnumerateArray().First();
            var itemId = item.GetProperty("id").GetGuid();
            using var detail = await client.GetAsync(root + $"/risk/{kind}/{itemId:D}/history");
            detail.EnsureSuccessStatusCode(); Assert.True(detail.Headers.CacheControl!.NoStore);
            var data = await ReadJson(detail); Assert.Equal(itemId, data.GetProperty("itemId").GetGuid());
            Assert.Equal(itemId, data.GetProperty("versions")[0].GetProperty("item").GetProperty("id").GetGuid());
            using var foreign = await client.GetAsync(root + $"/risk/{kind}/{Guid.NewGuid():D}/history");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using (var versions = await client.GetAsync($"/api/v1/terms/{version.TermId:D}/versions"))
        { versions.EnsureSuccessStatusCode(); Assert.Single((await ReadJson(versions)).GetProperty("versions").EnumerateArray()); }
        using (var same = await client.GetAsync(root + $"/compare?beforeVersionId={version.Id:D}&afterVersionId={version.Id:D}"))
        { same.EnsureSuccessStatusCode(); await Capture("PolicyVersionComparison", same); }
        foreach (var suffix in new[] { "/history?forged=true", "/history?effectiveAt=invalid&knownAt=invalid", $"/compare?beforeVersionId={version.Id:D}&afterVersionId={version.Id:D}&afterVersionId={version.Id:D}" })
        { using var invalid = await client.GetAsync(root + suffix); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using (var anonymous = host.CreateClient())
        using (var denied = await anonymous.GetAsync(root + "/history")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var compared = await client.GetAsync(root + $"/compare?beforeVersionId={version.Id:D}&afterVersionId={foreignVersion:D}"))
            Assert.Equal(HttpStatusCode.NotFound, compared.StatusCode);
        using (var terms = await client.GetAsync(root + $"/clone-terms?versionId={version.Id:D}&relationshipId={policy.RelationshipId:D}"))
        { terms.EnsureSuccessStatusCode(); Assert.Equal(termsId, (await ReadJson(terms)).GetProperty("termsId").GetGuid()); await Capture("PolicyCloneTerms", terms); }

        async Task<HttpResponseMessage> Post(string path, object body, bool csrfHeader = true, string? match = null, string? key = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            if (csrfHeader) request.Headers.Add("X-CSRF-Token", csrf);
            if (match != "missing") request.Headers.TryAddWithoutValidation("If-Match", match ?? etag);
            if (key != "missing") request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
            return await client.SendAsync(request);
        }
        var cloneBody = new { versionId = version.Id, relationshipId = policy.RelationshipId, confirmedTermsId = termsId, reason = "HTTP policy clone proof into incomplete quotation" };
        var exportPath = $"/api/v1/terms/{version.TermId:D}/as-at/export";
        var exportBody = new { effectiveAt = version.EffectiveAt, knownAt = f.Clock.GetUtcNow(), versionId = version.Id, contentHash = Convert.ToHexStringLower(version.ContentHash), reason = "HTTP reconstruction request retention proof" };
        foreach (var (path, body) in new (string, object)[] { (root + "/clone", cloneBody), (exportPath, exportBody) })
        {
            using (var response = await Post(path, body, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Post(path, body, match: "missing")) Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
            using (var response = await Post(path, body, key: "missing")) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await Post(path, body, match: "W/" + etag)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await Post(path + "?force=true", body)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await Post(path, new { forged = true })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var key = Guid.NewGuid().ToString();
            using var accepted = await Post(path, body, key: key); Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            Assert.True(accepted.Headers.CacheControl!.NoStore);
            using var replay = await Post(path, body, key: key); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(await accepted.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            var data = await ReadJson(accepted);
            await Capture(path == exportPath ? "PolicyReconstructionResult" : "PolicyCloneResult", accepted);
            if (path == exportPath)
                Assert.True(await db.Set<PolicyReconstructionRequest>().AnyAsync(x => x.Id == data.GetProperty("requestId").GetGuid()));
            else
                Assert.True(await db.Set<Quote>().AnyAsync(x => x.Id == data.GetProperty("quoteId").GetGuid() && x.State == "draft" && x.CurrentUnderwritingCycleId == null));
        }
        using var requests = await client.GetAsync(root + "/reconstructions");
        requests.EnsureSuccessStatusCode(); Assert.True(requests.Headers.CacheControl!.NoStore);
        var retained = await ReadJson(requests);
        Assert.Equal(3, retained.GetArrayLength());
        Assert.All(retained.EnumerateArray(), item => Assert.Equal("pending", item.GetProperty("state").GetString()));
        await Capture("PolicyReconstructionList", requests);
    }
}
