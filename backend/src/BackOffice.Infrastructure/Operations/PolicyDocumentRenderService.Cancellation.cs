using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class PolicyDocumentRenderService
{
    internal sealed record CancellationSource(CancellationConsequence Notice,Policy Policy,PolicyVersion Version);

    internal static async Task<CancellationSource> LoadCancellation(BackOfficeDbContext db,Guid noticeId,Guid policyId,Guid actorId,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Cancellation document provenance requires a held parent.");
        var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==noticeId && x.Kind=="notice" && x.PolicyId==policyId,token) ?? throw Missing();
        if(notice.CreatedBy!=actorId)throw Missing();
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==policyId,token);
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==notice.VersionId && x.PolicyId==policyId && x.TermId==notice.TermId && x.TransactionId==notice.TransactionId,token) ?? throw Missing();
        var decision=await db.Set<CancellationIssueDecision>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==notice.DecisionId && x.PolicyId==policyId && x.BaseTermId==notice.TermId,token) ?? throw Missing();
        if(!await db.Set<PolicyTransaction>().AnyAsync(x=>x.Id==notice.TransactionId && x.Kind=="cancellation" && x.CancellationIssueDecisionId==decision.Id && x.ServicingDraftId==decision.DraftId,token) ||
            !await db.Set<ServicingDraft>().AnyAsync(x=>x.Id==decision.DraftId && x.State=="issued" && x.IssuedTransactionId==notice.TransactionId,token))throw Invalid();
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==notice.WorkId,token);
        if(work.Kind!="cancellation-notice" || work.SubjectRecordId!=notice.Id || work.OperationKey!="cancellation-notice/"+notice.TransactionId.ToString("N") ||
            work.Payload!=notice.PayloadJson || Encoding.UTF8.GetByteCount(notice.PayloadJson)>1024*1024 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(notice.PayloadJson)),notice.PayloadHash) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(version.SnapshotJson)),version.ContentHash))throw Invalid();
        try
        {
            using var parsed=JsonDocument.Parse(notice.PayloadJson);using var snapshot=JsonDocument.Parse(version.SnapshotJson);
            var root=parsed.RootElement;var source=snapshot.RootElement;
            string[] fields=["format","intentId","kind","policyId","policyReference","termId","transactionId","versionId","contentHash","effectiveAt","reasonCode","reason","recipients","demo"];
            if(!Unique(root) || root.EnumerateObject().Count()!=fields.Length || root.EnumerateObject().Any(x=>!fields.Contains(x.Name,StringComparer.Ordinal)) ||
                !PolicySnapshotShape.Valid(source) || root.GetProperty("format").GetString()!="cancellation-consequence-1" || root.GetProperty("kind").GetString()!="notice" ||
                root.GetProperty("intentId").GetGuid()!=notice.Id || root.GetProperty("policyId").GetGuid()!=policyId || root.GetProperty("policyReference").GetString()!=policy.Reference ||
                root.GetProperty("termId").GetGuid()!=notice.TermId || root.GetProperty("transactionId").GetGuid()!=notice.TransactionId || root.GetProperty("versionId").GetGuid()!=version.Id ||
                root.GetProperty("contentHash").GetString()!=Convert.ToHexStringLower(version.ContentHash) || root.GetProperty("effectiveAt").GetDateTimeOffset()!=decision.EffectiveAt ||
                root.GetProperty("reason").GetString()!=decision.Reason || root.GetProperty("reasonCode").GetString()!=source.GetProperty("cancellation").GetProperty("reasonCode").GetString() ||
                root.GetProperty("recipients").ValueKind!=JsonValueKind.Array || !root.GetProperty("demo").GetBoolean() ||
                source.GetProperty("provenance").GetProperty("cancellationIssueDecisionId").GetGuid()!=decision.Id ||
                source.GetProperty("provenance").GetProperty("transactionId").GetGuid()!=notice.TransactionId || version.EffectiveAt!=decision.EffectiveAt)throw Invalid();
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){throw Invalid();}
        return new(notice,policy,version);
    }
}
