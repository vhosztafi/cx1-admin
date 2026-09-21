using System.Text;
using BackOffice.Api;
using BackOffice.Application.Operations;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalTaskHttpInputTests
{
    private static DefaultHttpContext Context(string body)
    {
        var context = new DefaultHttpContext(); context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body)); return context;
    }

    [Fact]
    public async Task ClosedTaskInputRejectsForgedFieldsDuplicatesNullsAndWrongCase()
    {
        const string valid = """{"typeCode":"complaint","title":"Review","priority":"normal","assignment":{"kind":"unassigned"}}""";
        var parsed = await TaskEndpoints.Input<TaskWrite>(Context(valid)); Assert.Equal("complaint", parsed.TypeCode);
        foreach (var invalid in new[] { valid.Replace("\"title\"", "\"Title\""), valid.Replace("\"Review\"", "null"),
            valid.Replace("\"title\":", "\"title\":\"First\",\"title\":"), valid[..^1] + ",\"state\":\"completed\"}",
            valid.Replace("\"kind\":\"unassigned\"", "\"kind\":\"unassigned\",\"agencyId\":\"11111111-1111-4111-8111-111111111111\""),
            valid.Replace("\"typeCode\":\"complaint\",", ""), valid[..^1] + ",\"subjectRecordId\":\"11111111-1111-4111-8111-111111111111\"}" })
            await Assert.ThrowsAsync<QuoteHttpException>(() => TaskEndpoints.Input<TaskWrite>(Context(invalid)));
    }

    [Fact]
    public async Task BulkRequiresVersionsAndBodySizeIsBounded()
    {
        await Assert.ThrowsAsync<QuoteHttpException>(() => TaskEndpoints.Input<TaskEndpoints.BulkCompleteInput>(Context("""{"tasks":[{"id":"11111111-1111-4111-8111-111111111111"}],"reason":"Reviewed"}""")));
        var tooLarge = "{\"body\":\"" + new string('x', 65536) + "\"}";
        Assert.Equal(413, (await Assert.ThrowsAsync<QuoteHttpException>(() => TaskEndpoints.Input<TaskEndpoints.CommentInput>(Context(tooLarge)))).Status);
        await Assert.ThrowsAsync<QuoteHttpException>(() => TaskEndpoints.Input<TaskEndpoints.SubjectInput>(Context("""{"kind":"agency"}""")));
    }
}
