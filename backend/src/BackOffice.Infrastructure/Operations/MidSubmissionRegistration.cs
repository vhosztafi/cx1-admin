using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed class MidSubmissionRegistration(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    // Reuse retained work and payload. Cancellation keeps its original cancellation scenario;
    // the separate submission records the selected deterministic MID provider configuration.
    public async Task<Guid> Register(Guid workId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var hint=await db.Set<OutboxWork>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==workId&&(x.Kind=="mid-update"||x.Kind=="cancellation-mid-removal"),token)??throw MidSnapshots.Missing();
        var snapshot=await MidSnapshots.Capture(db,workId,token);var actor=await MidAuthority.Sender(db,hint.CreatedBy,token);
        await OperationalScope.HoldParents(db,actor,[new("policy",snapshot.PolicyId)],"mid-retry",token);
        var existing=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==workId,token);
        if(existing is not null){await MidAuthority.Hold(db,actor,existing,"mid-retry",token);await tx.CommitAsync(token);return existing.Id;}
        var now=time.GetUtcNow();var setting=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==OperationalMidSeed.Scope&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token)??throw MidSnapshots.Invalid();
        if(OperationalMidSeed.Scenario(setting)is null)throw MidSnapshots.Invalid();
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={workId}").SingleAsync(token);
        existing=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==workId,token);
        if(existing is not null){await tx.CommitAsync(token);return existing.Id;}
        if(work.State!="pending"||work.Attempts!=0||work.Payload!=hint.Payload||work.CreatedBy!=actor.UserId||work.SubjectRecordId!=snapshot.IntentId)throw MidSnapshots.Invalid();
        if(work.Kind=="mid-update")work.ScenarioVersionId=setting.Id;
        else if(work.ScenarioVersionId is null)throw MidSnapshots.Invalid();
        // Cancellation reporting waits until cover actually ends; no early withdrawal.
        if(snapshot.Purpose=="cancellation"&&work.NextAttemptAt<snapshot.EffectiveAt)work.NextAttemptAt=snapshot.EffectiveAt;
        await db.SaveChangesAsync(token);
        var json=MidSnapshots.Serialize(snapshot);var row=new MidSubmission{PolicyId=snapshot.PolicyId,TermId=snapshot.TermId,TransactionId=snapshot.TransactionId,VersionId=snapshot.VersionId,BaseVersionId=snapshot.BaseVersionId,
            PolicyMidIntentId=work.Kind=="mid-update"?snapshot.IntentId:null,CancellationConsequenceId=work.Kind=="cancellation-mid-removal"?snapshot.IntentId:null,
            WorkId=work.Id,ScenarioVersionId=setting.Id,RequestJson=json,RequestHash=MidSnapshots.Hash(json),CreatedAt=now,CreatedBy=actor.UserId};
        db.Add(row);await db.SaveChangesAsync(token);await tx.CommitAsync(token);return row.Id;
    }
    // Explicit missing-only bridge for retained demo initial issues. Reads never enqueue work.
    public async Task<Platform.CommandOutcome> RegisterMissingInitial(Application.ActorContext actor,Guid versionId,string reason,string key,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length<10||reason.Length>1000)throw new OperationalAccessException(422,"mid-bridge-reason-required");
        PolicyVersion? version=null;PolicyTerm? term=null;
        return await new Platform.SqlCommandBoundary(factory,time).ExecuteAuthorizedAsync(new(actor.UserId,$"/internal/mid/versions/{versionId}/register-initial",key,Guid.NewGuid()),new{versionId,reason},"mid.initial-bridge",
            async(db,ct)=>
            {
                version=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId,ct)??throw MidSnapshots.Missing();
                await OperationalScope.HoldParents(db,actor,[new("policy",version.PolicyId)],"mid-retry",ct);
                var transaction=await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x=>x.Id==version.TransactionId,ct);
                using var source=System.Text.Json.JsonDocument.Parse(version.SnapshotJson);
                if(transaction.Kind!="new-business"||source.RootElement.GetProperty("productCode").GetString() is not("motor-trade-road-risks" or "motor-trade-combined")||MidSnapshots.Hash(version.SnapshotJson)!=Convert.ToHexStringLower(version.ContentHash))throw MidSnapshots.Invalid();
                term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==version.TermId&&x.PolicyId==version.PolicyId,ct);
            },async(db,ct)=>
            {
                // Parent graph authorization precedes the version serialization lock.
                await db.Set<PolicyVersion>().FromSqlInterpolated($"SELECT * FROM PolicyVersion WITH(UPDLOCK,HOLDLOCK) WHERE Id={versionId}").AsNoTracking().SingleAsync(ct);
                var intent=await InitialIntent(db,version!,term!,actor.UserId,time.GetUtcNow(),Guid.NewGuid(),ct);
                db.Add(new AuditEvent{ActorId=actor.UserId,SubjectRecordId=version!.PolicyId,EventType="mid.initial-bridge-reason",Reason=reason,OccurredAt=time.GetUtcNow(),After=MidSnapshots.Serialize(new{versionId,intentId=intent.Id})});
                await db.SaveChangesAsync(ct);return MessageDeliveryService.JobOutcome(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId,ct));
            },token);
    }
    public async Task RecordUnavailable(Guid id,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={id}").SingleAsync(token);
        if(work.Kind is not("mid-update" or "cancellation-mid-removal"))throw MidSnapshots.Missing();
        if(work.State=="pending"&&!await db.Set<MidSubmission>().AnyAsync(x=>x.WorkId==id,token))
        {await Platform.SqlJobLeases.MarkTerminalAsync(db,work,"mid-context-unavailable",time.GetUtcNow(),token);await db.SaveChangesAsync(token);}
        await tx.CommitAsync(token);
    }
    internal static async Task<PolicyMidIntent> InitialIntent(BackOfficeDbContext db,PolicyVersion version,PolicyTerm term,Guid actor,DateTimeOffset now,Guid correlation,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Initial MID requires held issue or bridge transaction.");
        var existing=await db.Set<PolicyMidIntent>().SingleOrDefaultAsync(x=>x.VersionId==version.Id&&x.Purpose=="new-business",token);if(existing is not null)return existing;
        var row=new PolicyMidIntent{PolicyId=version.PolicyId,TermId=term.Id,TransactionId=version.TransactionId,VersionId=version.Id,Purpose="new-business",CreatedAt=now,CreatedBy=actor};
        row.PayloadJson=MidSnapshots.Serialize(new{format="policy-mid-intent-1",intentId=row.Id,policyId=row.PolicyId,termId=term.Id,versionId=version.Id,contentHash=Convert.ToHexStringLower(version.ContentHash),action="add",effectiveAt=version.EffectiveAt,endsAt=term.EndsAt});
        row.PayloadHash=SHA256.HashData(Encoding.UTF8.GetBytes(row.PayloadJson));
        var work=new OutboxWork{Kind="mid-update",OperationKey=$"mid-update/{row.Id:N}",SubjectRecordId=row.Id,Payload=row.PayloadJson,CreatedAt=now,NextAttemptAt=now,CreatedBy=actor,CorrelationId=correlation};
        db.Add(work);await db.SaveChangesAsync(token);row.WorkId=work.Id;db.Add(row);await db.SaveChangesAsync(token);return row;
    }
}
