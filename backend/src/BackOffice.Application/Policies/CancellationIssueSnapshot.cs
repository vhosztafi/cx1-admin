using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackOffice.Application.Policies;

public sealed record CancellationSnapshotInput(Guid SourceQuoteId,Guid DecisionId,Guid ApprovalId,Guid PreviewId,
    Guid BaseVersionId,Guid RevisionId,Guid TransactionId,DateTimeOffset EffectiveAt,DateTimeOffset ProcessedAt,
    string PreviewHash,string ReasonCode,string RuleVersion);

public static class CancellationIssueSnapshot
{
    // Historical declarations and cumulative charges are retained. The separate
    // cancellation obligation records the credit; this snapshot records cover.
    public static string Create(JsonElement basis,CancellationSnapshotInput input)
    {
        if(input is null || !PolicySnapshotShape.Valid(basis) || basis.GetProperty("snapshotFormat").GetString() is "issued-cancellation-1" or "issued-commercial-cancellation-1" ||
            new[]{input.SourceQuoteId,input.DecisionId,input.ApprovalId,input.PreviewId,input.BaseVersionId,input.RevisionId,input.TransactionId}.Contains(Guid.Empty) ||
            input.EffectiveAt.Offset!=TimeSpan.Zero || input.ProcessedAt.Offset!=TimeSpan.Zero ||
            input.PreviewHash is null || input.PreviewHash.Length!=64 || input.PreviewHash.Any(c=>!char.IsAsciiDigit(c)&&c is not(>='a' and <='f')) ||
            !CancellationDecisionRules.Reasons.Any(x=>x.Code==input.ReasonCode) || input.RuleVersion!="demo-servicing-1")
            throw new ArgumentException("Cancellation requires a valid issued base and exact reviewed decision provenance.");
        var term=basis.GetProperty("term");
        if(input.EffectiveAt<term.GetProperty("startsAt").GetDateTimeOffset() || input.EffectiveAt>=term.GetProperty("endsAt").GetDateTimeOffset())
            throw new ArgumentException("Cancellation effective time must be inside the original half-open term.");
        var snapshot=JsonNode.Parse(basis.GetRawText())!.AsObject();
        snapshot["snapshotFormat"]=basis.GetProperty("productCode").GetString()=="commercial-combined"?"issued-commercial-cancellation-1":"issued-cancellation-1";
        snapshot["provenance"]=JsonSerializer.SerializeToNode(new {source="backoffice",sourceQuoteId=input.SourceQuoteId,
            cancellationIssueDecisionId=input.DecisionId,cancellationApprovalId=input.ApprovalId,cancellationPreviewId=input.PreviewId,
            baseVersionId=input.BaseVersionId,revisionId=input.RevisionId,transactionId=input.TransactionId,
            effectiveAt=input.EffectiveAt,processedAt=input.ProcessedAt,sliceOrdinal=1,inputHash=input.PreviewHash});
        snapshot["cancellation"]=JsonSerializer.SerializeToNode(new {outcome="cancelled",reasonCode=input.ReasonCode,
            effectiveAt=input.EffectiveAt,ruleVersion=input.RuleVersion});
        var json=snapshot.ToJsonString();using var parsed=JsonDocument.Parse(json);
        if(!PolicySnapshotShape.Valid(parsed.RootElement)) throw new InvalidOperationException("Cancellation snapshot violates its immutable contract.");
        return json;
    }
}
