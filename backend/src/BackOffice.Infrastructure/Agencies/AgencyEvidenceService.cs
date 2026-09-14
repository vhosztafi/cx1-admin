using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencyEvidenceConfiguration(Guid Id,decimal MinimumPi,string TobaVersion,int CheckValidityDays,IReadOnlyDictionary<string,string> Scenarios);
public sealed record AgencyReadiness(bool Valid,string AgencyEtag,Guid RuleVersionId,DateTimeOffset CalculatedAt,IReadOnlyList<AgencyChecklistItem> Items);
public sealed class AgencyEvidenceService(SqlCommandBoundary commands,AgencyDraftService drafts,TimeProvider time)
{
    public async Task<CommandOutcome> Upload(ActorContext actor,Guid agencyId,string key,byte[] version,AgencyFileInput file,CancellationToken token)
    {
        file=AgencyEvidenceRules.ValidateFile(file.FileName,file.ContentType,file.Content);
        await drafts.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/evidence-files",key,Guid.NewGuid()),new{file.FileName,file.ContentType,file.Sha256,byteLength=file.Content.Length},"agency.evidence-file-uploaded",async(db,ct)=>
        {
            var agency=await WritableAgency(db,agencyId,version,ct);
            var row=new AgencyEvidenceFile{AgencyId=agencyId,FileName=file.FileName,ContentType=file.ContentType,Content=file.Content,ByteLength=file.Content.Length,Sha256=file.Sha256,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};
            db.Add(row);Touch(db,agency,actor.UserId,"agency.evidence-file-uploaded");await db.SaveChangesAsync(ct);return Receipt(row.Id,agency,201);
        },token);
    }
    public async Task<CommandOutcome> Attest(ActorContext actor,Guid agencyId,string key,byte[] version,string kind,Guid fileId,string notes,DateOnly? expiresOn,CancellationToken token)
    {
        if(!AgencyEvidenceRules.AttestationKinds.Contains(kind)||fileId==Guid.Empty||string.IsNullOrWhiteSpace(notes)||notes.Length>1000||notes.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))throw new AgencyCommandException(422,"evidence-attestation-input");
        await drafts.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/evidence",key,Guid.NewGuid()),new{kind,fileId,notes,expiresOn},"agency.evidence-recorded",async(db,ct)=>
        {
            var agency=await WritableAgency(db,agencyId,version,ct);var rule=await Configuration(db,ct);
            if(!await db.Set<AgencyEvidenceFile>().AnyAsync(x=>x.Id==fileId&&x.AgencyId==agencyId&&x.ScreeningState=="demo-cleared",ct))throw new AgencyCommandException(422,"evidence-file-unavailable");
            using var input=await Draft(db,agencyId,ct);var today=BusinessDate(time.GetUtcNow());
            var valid=AgencyEvidenceRules.AttestationValid(input.RootElement,kind,today,rule.MinimumPi,rule.TobaVersion,expiresOn);
            var evidence=Evidence(agencyId,kind,input.RootElement,rule.Id,valid?"verified":"rejected",valid?"demo-attested":"demo-declarations-incomplete",actor.UserId,expiresOn);
            evidence.FileId=fileId;evidence.AttestedBy=actor.UserId;evidence.Notes=notes.Trim();db.Add(evidence);
            Touch(db,agency,actor.UserId,"agency.evidence-recorded");await db.SaveChangesAsync(ct);return Receipt(evidence.Id,agency,201);
        },token);
    }
    public async Task<CommandOutcome> Check(ActorContext actor,Guid agencyId,string key,byte[] version,string kind,CancellationToken token)
    {
        if(!AgencyEvidenceRules.CheckKinds.Contains(kind))throw new AgencyCommandException(422,"evidence-check-kind");
        await drafts.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/checks",key,Guid.NewGuid()),new{kind},"agency.check-completed",async(db,ct)=>
        {
            var agency=await WritableAgency(db,agencyId,version,ct);var rule=await Configuration(db,ct);using var input=await Draft(db,agencyId,ct);
            var scenario=rule.Scenarios.GetValueOrDefault(kind,"unavailable");var outcome=AgencyEvidenceRules.DemoCheck(input.RootElement,kind,scenario);
            var now=time.GetUtcNow();var resultCode="demo-"+outcome;
            var evidence=Evidence(agencyId,kind,input.RootElement,rule.Id,outcome=="pass"?"verified":outcome=="refer"?"rejected":"unavailable",resultCode,actor.UserId,BusinessDate(now).AddDays(rule.CheckValidityDays));db.Add(evidence);
            var attempt=new AgencyCheckAttempt{AgencyId=agencyId,Kind=kind,State=outcome=="pass"?"passed":outcome,InputFingerprint=evidence.InputFingerprint,RuleVersionId=rule.Id,Scenario=scenario,EvidenceId=evidence.Id,CompletedAt=now,ResultCode=resultCode,CreatedBy=actor.UserId,CreatedAt=now};db.Add(attempt);
            Touch(db,agency,actor.UserId,"agency.check-completed");await db.SaveChangesAsync(ct);return Receipt(attempt.Id,agency,202);
        },token);
    }
    public async Task<AgencyEvidenceConfiguration> Configuration(BackOfficeDbContext db,CancellationToken token)
    {
        var row=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope=="agency-compliance"&&x.EffectiveFrom<=time.GetUtcNow()).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token)??throw new AgencyCommandException(503,"evidence-rules-unavailable");
        using var document=JsonDocument.Parse(row.Values);var values=document.RootElement;
        if(!values.TryGetProperty("demo",out var demo)||demo.ValueKind!=JsonValueKind.True||!values.TryGetProperty("minimumPi",out var minimum)||!decimal.TryParse(minimum.GetString(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var limit)||limit<=0||!values.TryGetProperty("checkValidityDays",out var days)||!days.TryGetInt32(out var validity)||validity is <1 or >365||!values.TryGetProperty("tobaVersion",out var toba)||string.IsNullOrWhiteSpace(toba.GetString())||!values.TryGetProperty("scenarios",out var scenarios)||scenarios.ValueKind!=JsonValueKind.Object)throw new AgencyCommandException(503,"evidence-demo-configuration");
        var parsed=new Dictionary<string,string>();foreach(var kind in AgencyEvidenceRules.CheckKinds){var value=scenarios.TryGetProperty(kind,out var scenario)&&scenario.ValueKind==JsonValueKind.String?scenario.GetString():null;if(value is not ("pass" or "refer" or "unavailable"))throw new AgencyCommandException(503,"evidence-demo-configuration");parsed[kind]=value;}
        return new(row.Id,limit,toba.GetString()!,validity,parsed);
    }
    // Caller holds the agency read/write transaction; every evidence write takes
    // the same parent lock so details, evidence and returned ETag stay coherent.
    public async Task<AgencyReadiness> Validate(BackOfficeDbContext db,Agency agency,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Agency validation requires an aggregate transaction.");
        var rule=await Configuration(db,token);using var input=await Draft(db,agency.Id,token);var now=time.GetUtcNow();var today=BusinessDate(now);
        var latest=await db.Set<AgencyEvidence>().AsNoTracking().Where(x=>x.AgencyId==agency.Id&&!db.Set<AgencyEvidence>().Any(newer=>newer.AgencyId==x.AgencyId&&newer.Kind==x.Kind&&newer.Ordinal>x.Ordinal)).ToListAsync(token);
        var facts=latest.ToDictionary(x=>x.Kind,x=>new AgencyEvidenceFact(x.Id,x.Kind,x.State,x.InputFingerprint,x.RuleVersionId,x.ExpiresOn));
        var hasCurrentSelection=await db.Set<AgencyDraftProduct>().AnyAsync(x=>x.AgencyId==agency.Id&&x.EffectiveFrom<=today,token);
        // 04-05/06 must replace unavailable with actual scoped identity and
        // effective distribution-setting checks. A draft catalog is not a grant.
        var items=AgencyActivationRules.Evaluate(input.RootElement,facts,rule.Id,today,rule.MinimumPi,rule.TobaVersion,new(hasCurrentSelection?null:false,null)).ToList();
        if(agency.RelationshipManagerId is Guid manager&&!await AgencyDraftService.Managers(db).AnyAsync(x=>x.Id==manager,token))
        {
            var index=items.FindIndex(x=>x.Code=="field-relationshipManagerId");items[index]=items[index] with{State="failed",Message="Choose a currently active internal relationship manager."};
        }
        if(agency.State=="abandoned")items.Add(new("agency-abandoned","/state",6,"failed","This abandoned draft is retained for history and cannot be activated."));
        return new(items.All(x=>x.State=="satisfied"),AgencyDraftService.Etag(agency.RowVersion),rule.Id,now,items);
    }
    private AgencyEvidence Evidence(Guid agencyId,string kind,JsonElement input,Guid rule,string state,string result,Guid actor,DateOnly? expiresOn)=>new(){AgencyId=agencyId,Kind=kind,State=state,InputSnapshot=AgencyEvidenceRules.Snapshot(input,kind),InputFingerprint=AgencyEvidenceRules.Fingerprint(input,kind),RuleVersionId=rule,VerifiedAt=state=="verified"?time.GetUtcNow():null,ExpiresOn=expiresOn,ResultCode=result,CreatedBy=actor,CreatedAt=time.GetUtcNow()};
    private static async Task<Agency> WritableAgency(BackOfficeDbContext db,Guid id,byte[] version,CancellationToken token){var agency=await AgencyDraftService.Lock(db,id,version,token);if(agency.State=="abandoned")throw new AgencyCommandException(409,"agency-abandoned");return agency;}
    private static async Task<JsonDocument> Draft(BackOfficeDbContext db,Guid id,CancellationToken token)=>JsonDocument.Parse(await db.Set<AgencyOnboarding>().Where(x=>x.AgencyId==id).Select(x=>x.Details).SingleAsync(token));
    private void Touch(BackOfficeDbContext db,Agency agency,Guid actor,string action){db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;db.Add(new AgencyActivity{AgencyId=agency.Id,ActorId=actor,Action=action,OccurredAt=time.GetUtcNow(),CreatedBy=actor});}
    private static CommandOutcome Receipt(Guid id,Agency agency,int status)=>new(id,status,JsonSerializer.Serialize(new{id}),Etag:AgencyDraftService.Etag(agency.RowVersion));
    private static DateOnly BusinessDate(DateTimeOffset instant)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
}
