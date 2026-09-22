using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class IncidentService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,IncidentOccurrenceResolver resolver,TimeProvider time)
{
    private static readonly JsonSerializerOptions Json=IncidentRules.Json;
    public async Task<CommandOutcome> Create(ActorContext actor,JsonElement draft,string key,CancellationToken token)
    {
        IncidentRules.Draft(draft);var policyId=draft.GetProperty("policyId").GetGuid();HeldOperationalScope? held=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,"/api/v1/incidents",key,Guid.NewGuid()),draft,"incident.draft-created",
            async(db,ct)=>{held=await HoldPolicy(db,actor,policyId,"incident-write",ct);await ValidateOwned(db,actor,policyId,draft,ct);},async(db,ct)=>
            {
                var now=time.GetUtcNow();var row=new OperationalIncident{PolicyId=policyId,ProductCode=draft.GetProperty("productCode").GetString()!,CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();command.CommandText="SELECT NEXT VALUE FOR IncidentReferenceSequence";
                row.Reference="INC-"+Convert.ToInt64(await command.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture).ToString("D7",CultureInfo.InvariantCulture);
                db.Add(row);await db.SaveChangesAsync(ct);
                await Append(db,row,draft,"Draft created",held!.ActorLabel,actor.UserId,ct);
                return await Outcome(db,row,201,ct);
            },token);
    }
    private async Task Append(BackOfficeDbContext db,OperationalIncident row,JsonElement draft,string reason,string author,Guid actor,CancellationToken token)
    {
        var number=1+(await db.Set<IncidentRevision>().Where(x=>x.IncidentId==row.Id).MaxAsync(x=>(int?)x.Number,token)??0);
        var json=JsonSerializer.Serialize(draft,Json);var revision=new IncidentRevision{IncidentId=row.Id,Number=number,DraftJson=json,ContentHash=Hash(json),Reason=reason,AuthorLabel=author,CreatedBy=actor,CreatedAt=time.GetUtcNow()};
        db.Add(revision);await db.SaveChangesAsync(token);
        foreach(var id in Evidence(draft))db.Add(new IncidentEvidence{RevisionId=revision.Id,DocumentVersionId=id,CreatedBy=actor,CreatedAt=time.GetUtcNow()});
        row.CurrentRevisionId=revision.Id;row.CurrentResolutionId=null;row.State="draft";row.UpdatedAt=time.GetUtcNow();await db.SaveChangesAsync(token);
    }
    private static Task<HeldOperationalScope> HoldPolicy(BackOfficeDbContext db,ActorContext actor,Guid policyId,string capability,CancellationToken token)
        =>OperationalScope.HoldParents(db,actor,[new("policy",policyId)],capability,token);
    private async Task ValidateOwned(BackOfficeDbContext db,ActorContext actor,Guid policyId,JsonElement draft,CancellationToken token)
    {
        if(draft.GetProperty("policyId").GetGuid()!=policyId)throw new OperationalAccessException(409,"incident-policy-immutable");
        var product=await(from policy in db.Set<Policy>() join definition in db.Set<Product>() on policy.ProductId equals definition.Id where policy.Id==policyId select definition.Code).SingleAsync(token);
        if(product!=draft.GetProperty("productCode").GetString())throw new IncidentRuleException("incident-product-mismatch");
        var subject=Subject(draft);
        if(subject is JsonElement supplied)
        {
            var history=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.PolicyId==policyId&&x.ProcessedAt<=time.GetUtcNow()).Select(x=>x.SnapshotJson).ToArrayAsync(token);
            var owned=false;
            foreach(var json in history)
            {
                using var snapshot=JsonDocument.Parse(json);
                try{IncidentSubjectRules.Ready(snapshot.RootElement,supplied);owned=true;break;}catch(IncidentOccurrenceException){ }
            }
            if(!owned)throw new IncidentRuleException("incident-subject-not-owned");
        }
        var evidence=Evidence(draft);
        if(evidence.Distinct().Count()!=evidence.Length)throw new IncidentRuleException("incident-evidence-duplicate");
        foreach(var id in evidence.Order())
        {
            var parent=await(from version in db.Set<DocumentVersion>() join document in db.Set<OperationalDocument>() on version.DocumentId equals document.Id
                join original in db.Set<OperationalSubject>() on document.SubjectId equals original.Id where version.Id==id select original).AsNoTracking().SingleOrDefaultAsync(token);
            if(parent?.Kind!="policy"||parent.PolicyId!=policyId)throw Missing();
            await DocumentService.AttachmentVersion(db,actor,parent.Id,id,true,token);
        }
    }
    private static JsonElement? Subject(JsonElement draft)=>draft.TryGetProperty(draft.GetProperty("productCode").GetString()=="commercial-combined"?"commercialSubject":"motorSubject",out var subject)?subject:null;
    private static Guid[] Evidence(JsonElement draft)=>draft.TryGetProperty("evidenceDocumentVersionIds",out var ids)?ids.EnumerateArray().Select(x=>x.GetGuid()).ToArray():[];
    private static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static OperationalAccessException Missing()=>new(404,"incident-not-found");
    private static async Task<CommandOutcome> Outcome(BackOfficeDbContext db,OperationalIncident row,int status,CancellationToken token)
    {
        var revision=await db.Set<IncidentRevision>().AsNoTracking().SingleAsync(x=>x.Id==row.CurrentRevisionId,token);
        using var draft=JsonDocument.Parse(revision.DraftJson);IncidentOccurrenceRecord? resolution=null;
        if(row.CurrentResolutionId is Guid id)resolution=await db.Set<IncidentOccurrenceRecord>().AsNoTracking().SingleAsync(x=>x.Id==id,token);
        using var resolutionDocument=resolution is null?null:JsonDocument.Parse(resolution.ResolutionJson);
        var value=resolutionDocument?.RootElement.Clone();
        var missing=IncidentRules.Missing(draft.RootElement,resolution?.State??"incomplete",Subject(draft.RootElement)is not null&&resolution?.State=="resolved");
        var handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleOrDefaultAsync(x=>x.RevisionId==revision.Id,token);
        JsonElement? administratorSummary=null;string? administratorName=null;
        if(handoff is not null)
        {
            using var submitted=JsonDocument.Parse(handoff.RequestJson);
            administratorName=submitted.RootElement.GetProperty("administratorName").GetString();
            var summary=await db.Set<ClaimsSummary>().AsNoTracking().Where(x=>x.HandoffId==handoff.Id).OrderByDescending(x=>x.AsOf).ThenByDescending(x=>x.ReceivedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(token);
            if(summary is not null)
            {
                var external=JsonSerializer.Deserialize<ClaimsAdministratorSummary>(summary.SummaryJson,ClaimsSnapshots.Json)!;
                administratorSummary=JsonSerializer.SerializeToElement(new{summary.Id,incidentId=row.Id,summary.HandoffId,summary.AsOf,summary.ReceivedAt,external.Status,external.Paid,external.Reserved,external.Currency,external.ProviderReference,external.Liability,external.Incurred,external.RecoveryExpected,external.ExcessApplied,external.MovementNote,providerEventId=summary.ProviderEventId},ClaimsSnapshots.Json);
            }
        }
        return new(row.Id,status,JsonSerializer.Serialize(new{id=row.Id,row.Reference,revisionId=revision.Id,draft=draft.RootElement,row.State,resolution=value,row.CreatedAt,row.UpdatedAt,missing,providerReference=handoff?.ProviderReference,administratorName,administratorSummary},Json),Etag:TaskService.Etag(row.RowVersion));
    }
}
