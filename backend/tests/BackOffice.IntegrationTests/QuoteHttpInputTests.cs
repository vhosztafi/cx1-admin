using System.Text;
using BackOffice.Api;
using BackOffice.Application.Quotes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class QuoteHttpInputTests
{
    [Fact]
    public async Task LookupCommandsRejectCallerQueriesAndAmbiguousSelections()
    {
        var revision = Guid.NewGuid(); var lookup = Guid.NewGuid(); var candidate = Guid.NewGuid();
        var fingerprint = new string('a', 64);
        var request = await QuoteLookupHttpInput.RequestAsync(Request($$"""{"revisionId":"{{revision}}","kind":"address","scope":"insured","scenario":"success"}"""));
        Assert.Equal(revision, request.RevisionId); Assert.Null(request.Target.RiskItemId);
        var selection = await QuoteLookupHttpInput.SelectionAsync(Request($$"""{"lookupId":"{{lookup}}","revisionId":"{{revision}}","inputFingerprint":"{{fingerprint}}","candidateId":"{{candidate}}"}"""));
        Assert.Equal(candidate, selection.CandidateId);
        foreach (var extra in new[] { "\"query\":\"AB12CD\"", "\"url\":\"https://example.invalid\"", "\"kind\":\"vehicle\"", "\"verified\":true" })
            await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteLookupHttpInput.RequestAsync(Request($$"""{"revisionId":"{{revision}}","kind":"address","scope":"insured","scenario":"success",{{extra}}}""")));
        foreach (var decision in new[] { "", ",\"candidateId\":\"" + candidate + "\",\"manualReason\":\"Both\"", ",\"manualReason\":\" \"", ",\"candidateId\":null" })
            await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteLookupHttpInput.SelectionAsync(Request($$"""{"lookupId":"{{lookup}}","revisionId":"{{revision}}","inputFingerprint":"{{fingerprint}}"{{decision}}}""")));
        await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteLookupHttpInput.SelectionAsync(Request($$"""{"lookupId":"{{lookup}}","revisionId":"{{revision}}","inputFingerprint":"{{new string('G',64)}}","manualReason":"Checked"}""")));
    }

    [Fact]
    public void ProductSelectionRequiresOneExactRelationshipQuery()
    {
        var id = Guid.NewGuid(); var request = new DefaultHttpContext().Request;
        request.QueryString = new QueryString($"?relationshipId={id:D}");
        Assert.Equal(id, QuoteHttpInput.ProductRelationship(request));
        foreach (var query in new[] { "", "?relationshipId=", $"?RelationshipId={id:D}", $"?relationshipId={id:N}",
            "?relationshipId=00000000-0000-0000-0000-000000000000", $"?relationshipId=%20{id:D}",
            $"?relationshipId={id:D}&relationshipId={id:D}", $"?relationshipId={id:D}&agencyId={id:D}",
            $"?relationshipId={id:D}&RelationshipId={id:D}" })
        {
            request.QueryString = new QueryString(query);
            var error = Assert.Throws<QuoteHttpException>(() => QuoteHttpInput.ProductRelationship(request));
            Assert.Equal(400, error.Status); Assert.Equal("invalid-query", error.Code);
        }
    }

    private const string Create = "{\"relationshipId\":\"aaaaaaaa-0000-4000-8000-000000000001\",\"productVersionId\":\"aaaaaaaa-0000-4000-8000-000000000002\"}";
    private static HttpRequest Request(string json, bool length = true)
    {
        var request = new DefaultHttpContext().Request; request.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json); request.Body = new MemoryStream(bytes);
        if (length) request.ContentLength = bytes.Length;
        return request;
    }

    [Fact]
    public async Task CreateAllowsOmissionWhileSaveRetainsRawProposalAndReason()
    {
        var create = await QuoteHttpInput.CreateAsync(Request(Create)); Assert.Null(create.Proposal); Assert.Null(create.MatchSubmissionId);
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"), create.RelationshipId);
        const string proposal = "{ \"schemaVersion\":\"1.0\", \"productCode\":\"motor-trade-road-risks\" }";
        var save = await QuoteHttpInput.SaveAsync(Request("{\"proposal\":" + proposal + ",\"reason\":\"Fictional change\"}"));
        Assert.Equal(proposal, save.Proposal); Assert.Equal("Fictional change", save.Reason);
        var match = Guid.NewGuid();
        var linked = await QuoteHttpInput.CreateAsync(Request(Create[..^1] + ",\"matchSubmissionId\":\"" + match + "\"}"));
        Assert.Equal(match, linked.MatchSubmissionId); // Parsing does not authorize or implement attachment.
    }

    [Theory]
    [InlineData("{}", 422, "proposal-required")]
    [InlineData("{\"proposal\":null}", 422, "null-field")]
    [InlineData("{\"proposal\":[]}", 422, "invalid-proposal")]
    [InlineData("{\"Proposal\":{}}", 400, "unknown-field")]
    [InlineData("{\"proposal\":{},\"state\":\"issued\"}", 400, "unknown-field")]
    [InlineData("{\"proposal\":{},\"proposal\":{}}", 400, "duplicate-field")]
    [InlineData("{\"proposal\":{\"a\":1,\"\\u0061\":2}}", 400, "duplicate-field")]
    [InlineData("{\"proposal\":{\"items\":[null]}}", 422, "null-field")]
    [InlineData("{\"proposal\":{\"name\":\"\\ud800\"}}", 400, "invalid-json")]
    [InlineData("{\"proposal\":{\"\\ud800\":1}}", 400, "invalid-json")]
    [InlineData("{\"proposal\":{},\"reason\":\" \"}", 422, "invalid-reason")]
    [InlineData("{\"proposal\":{},}", 400, "invalid-json")]
    [InlineData("[]", 400, "object-required")]
    public async Task MalformedSaveEnvelopesFailWithSafeCodes(string json, int status, string code)
    {
        var error = await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.SaveAsync(Request(json)));
        Assert.Equal(status, error.Status); Assert.Equal(code, error.Code); Assert.DoesNotContain(json, error.Message);
    }

    [Fact]
    public async Task CreateRejectsMissingNilNonstandardAndWronglyCasedIdentities()
    {
        foreach (var json in new[] { "{}", Create.Replace("aaaaaaaa-0000-4000-8000-000000000001", Guid.Empty.ToString()),
            Create.Replace("aaaaaaaa-0000-4000-8000-000000000001", " aaaaaaaa-0000-4000-8000-000000000001 "),
            Create.Replace("aaaaaaaa-0000-4000-8000-000000000001", "aaaaaaaa000040008000000000000001"), Create.Replace("relationshipId", "RelationshipId") })
            await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.CreateAsync(Request(json)));
    }

    [Fact]
    public async Task ByteLimitAppliesToDeclaredAndStreamingBodiesWithoutTrustingContentLength()
    {
        var maximum = Create.PadLeft(QuoteCanonicalJson.MaximumBytes);
        Assert.NotNull(await QuoteHttpInput.CreateAsync(Request(maximum, length: false)));
        foreach (var declared in new[] { true, false })
        {
            var error = await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.CreateAsync(Request(maximum + " ", declared)));
            Assert.Equal(413, error.Status);
        }
        var deceptive = Request(maximum + " "); deceptive.ContentLength = 1;
        Assert.Equal(413, (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.CreateAsync(deceptive))).Status);
        var unicode = Request("{\"proposal\":{\"text\":\"" + new string('\u20ac', 350000) + "\"}}", length: false);
        Assert.Equal(413, (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.SaveAsync(unicode))).Status);
    }

    [Fact]
    public async Task InvalidUtf8MediaTypeDepthAndQueryFailClosed()
    {
        var invalid = Request("{}"); invalid.Body = new MemoryStream(new byte[] { 0xff, 0xfe }); invalid.ContentLength = 2;
        Assert.Equal("invalid-json", (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.SaveAsync(invalid))).Code);
        var media = Request(Create); media.ContentType = "text/plain";
        Assert.Equal(415, (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.CreateAsync(media))).Status);
        var query = Request(Create); query.QueryString = new QueryString("?agencyId=forged");
        Assert.Equal("invalid-query", (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.CreateAsync(query))).Code);
        var deep = "{\"proposal\":" + new string('[', 65) + "0" + new string(']', 65) + "}";
        Assert.Equal("invalid-json", (await Assert.ThrowsAsync<QuoteHttpException>(() => QuoteHttpInput.SaveAsync(Request(deep)))).Code);
    }

    [Fact]
    public void KeysAndStrongRowversionEtagsAreSingleCanonicalHeaders()
    {
        var request = Request(Create);
        Assert.Equal(428, Assert.Throws<QuoteHttpException>(() => QuoteHttpInput.Version(request)).Status);
        Assert.Throws<QuoteHttpException>(() => QuoteHttpInput.Key(request));
        request.Headers["Idempotency-Key"] = "abcdefghijklmnop"; Assert.Equal("abcdefghijklmnop", QuoteHttpInput.Key(request));
        request.Headers["Idempotency-Key"] = new StringValues(new[] { "abcdefghijklmnop", "qrstuvwxyz012345" });
        Assert.Throws<QuoteHttpException>(() => QuoteHttpInput.Key(request));
        foreach (var value in new[] { "*", "W/\"AAAAAAAAAAA=\"", "\"AAAAAAAAAAA=\",\"AAAAAAAAAAA=\"", "\"bad\"" })
        {
            request.Headers.IfMatch = value; Assert.Throws<QuoteHttpException>(() => QuoteHttpInput.Version(request));
        }
        request.Headers.IfMatch = "\"AAAAAAAAAAA=\""; Assert.Equal(new byte[8], QuoteHttpInput.Version(request));
    }
}
