using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
internal sealed record MidSnapshot(string Format,Guid IntentId,Guid PolicyId,Guid TermId,Guid TransactionId,Guid VersionId,string ContentHash,Guid? BaseVersionId,string? BaseContentHash,string Purpose,DateTimeOffset EffectiveAt,DateTimeOffset EndsAt,IReadOnlyList<MidItem> Items);
internal static class MidSnapshots
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Json);
    internal static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static OperationalAccessException Invalid()=>new(409,"mid-source-unavailable");
    internal static OperationalAccessException Missing()=>new(404,"mid-submission-not-found");
    internal static async Task<MidSnapshot> Capture(BackOfficeDbContext db,Guid workId,CancellationToken token)
    {
        var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==workId,token);
        var consequence=await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==workId&&x.Kind=="mid-removal",token);
        if((intent is null)==(consequence is null))throw Invalid();
        var versionId=intent?.VersionId??consequence!.VersionId;var policyId=intent?.PolicyId??consequence!.PolicyId;var transactionId=intent?.TransactionId??consequence!.TransactionId;
        var termId=intent?.TermId??consequence!.TermId;var id=intent?.Id??consequence!.Id;var payload=intent?.PayloadJson??consequence!.PayloadJson;var hash=intent?.PayloadHash??consequence!.PayloadHash;
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId&&x.PolicyId==policyId&&x.TermId==termId&&x.TransactionId==transactionId,token)??throw Invalid();
        var transaction=await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x=>x.Id==transactionId,token);var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==termId,token);
        var purpose=intent?.Purpose??"cancellation";if(transaction.Kind!=purpose||Hash(payload)!=Convert.ToHexStringLower(hash))throw Invalid();
        using var envelope=JsonDocument.Parse(payload);var root=envelope.RootElement;
        if(root.GetProperty("intentId").GetGuid()!=id||root.GetProperty("versionId").GetGuid()!=version.Id||root.GetProperty("policyId").GetGuid()!=policyId||root.GetProperty("termId").GetGuid()!=termId||
            root.GetProperty("contentHash").GetString()!=Convert.ToHexStringLower(version.ContentHash)||root.GetProperty("effectiveAt").GetDateTimeOffset()!=version.EffectiveAt)throw Invalid();
        Guid? basisId=null;
        if(purpose=="cancellation")basisId=await db.Set<CancellationIssueDecision>().Where(x=>x.Id==transaction.CancellationIssueDecisionId).Select(x=>x.BaseVersionId).SingleAsync(token);
        else if(purpose!="new-business")
        {
            if(version.SliceOrdinal>1)basisId=await db.Set<PolicyVersion>().Where(x=>x.TransactionId==transaction.Id&&x.SliceOrdinal==version.SliceOrdinal-1).Select(x=>x.Id).SingleAsync(token);
            else basisId=await db.Set<ServicingIssueDecision>().Where(x=>x.Id==transaction.ServicingIssueDecisionId).Select(x=>x.BaseVersionId).SingleAsync(token);
        }
        var basis=basisId is{} previous?await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==previous&&x.PolicyId==policyId,token):null;
        if(basisId is not null&&basis is null||Hash(version.SnapshotJson)!=Convert.ToHexStringLower(version.ContentHash)||basis is not null&&Hash(basis.SnapshotJson)!=Convert.ToHexStringLower(basis.ContentHash))throw Invalid();
        using var source=JsonDocument.Parse(version.SnapshotJson);using var old=basis is null?null:JsonDocument.Parse(basis.SnapshotJson);
        IReadOnlyList<MidItem> items;try{items=MidRules.Items(source.RootElement,old?.RootElement,purpose,version.EffectiveAt,term.EndsAt);}catch(MidRuleException){throw Invalid();}
        return new("mid-submission-2",id,policyId,termId,transactionId,versionId,Convert.ToHexStringLower(version.ContentHash),basisId,basis is null?null:Convert.ToHexStringLower(basis.ContentHash),purpose,version.EffectiveAt,term.EndsAt,items);
    }
}
