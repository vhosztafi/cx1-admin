using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public Task<CommandOutcome> ChaseAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid caseId, Guid submissionId,
        byte[] version, byte[] caseVersion, Guid lease, string body, string reason, string key, Guid correlation,
        CancellationToken token = default)
    {
        if (draftId == Guid.Empty || cycleId == Guid.Empty || caseId == Guid.Empty || submissionId == Guid.Empty || lease == Guid.Empty ||
            version is null || version.Length != 8 || caseVersion is null || caseVersion.Length != 8 ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new QuoteOperationException(422,"servicing-capacity-correspondence-invalid");
        body = Correspondence(body); reason = QuoteRatingService.Reason(reason);
        version = version.ToArray(); caseVersion = caseVersion.ToArray();
        ServicingDecisionContext? held = null; ServicingCapacityCase? capacity = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/capacity/{caseId:D}/chases",key,correlation),
            new { draftId,cycleId,caseId,submissionId,version=Convert.ToBase64String(version),caseVersion=Convert.ToBase64String(caseVersion),lease,body,reason },
            "servicing.capacity-chased",
            async(db,ct)=>
            {
                held=await HoldEscalationAuthority(db,actor,draftId,cycleId,lease,ct);
                capacity=await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={caseId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-capacity-case-not-found");
                if(capacity.State=="superseded" || capacity.ProviderId!=held.Scope.Eligible.BinderVersion.ProviderId || capacity.BinderVersionId!=held.Cycle.BinderVersionId)
                    throw new QuoteOperationException(409,"servicing-capacity-case-stale");
                if(!await db.Set<ServicingCapacitySubmission>().AnyAsync(x=>x.Id==submissionId && x.CaseId==caseId && x.DraftId==draftId && x.CycleId==cycleId,ct))
                    throw new QuoteOperationException(404,"servicing-capacity-submission-not-found");
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(!CryptographicOperations.FixedTimeEquals(capacity!.RowVersion,caseVersion)) throw new QuoteOperationException(412,"servicing-capacity-case-stale");
                if(capacity.CurrentSubmissionId!=submissionId || capacity.State is not("queued" or "sent" or "queried"))
                    throw new QuoteOperationException(409,"servicing-capacity-correspondence-state");
                var now=time.GetUtcNow();var message=await AddCorrespondence(db,capacity,submissionId,"chase",body,actor.UserId,now,ct);
                db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,SubjectRecordId=caseId,
                    EventType="servicing.capacity-correspondence-detail",Reason=reason,After=JsonSerializer.Serialize(new {messageId=message.Id,submissionId,kind="chase"}),CorrelationId=correlation});
                return await held.Receipt(db,message.Id,201,now,ct);
            },token);
    }

    private static async Task<ServicingCapacityMessage> AddCorrespondence(BackOfficeDbContext db,ServicingCapacityCase capacity,
        Guid submissionId,string kind,string body,Guid actor,DateTimeOffset now,CancellationToken token)
    {
        var row=new ServicingCapacityMessage{SubmissionId=submissionId,CaseId=capacity.Id,DraftId=capacity.DraftId,
            CycleId=capacity.CycleId,RevisionId=capacity.RevisionId,RatingId=capacity.RatingId,
            Sequence=checked((await db.Set<ServicingCapacityMessage>().Where(x=>x.CaseId==capacity.Id).MaxAsync(x=>(int?)x.Sequence,token)??0)+1),
            Kind=kind,Body=body,ContentHash=SHA256.HashData(Encoding.UTF8.GetBytes(body)),RecordedBy=actor,RecordedAt=now,CreatedBy=actor,CreatedAt=now};
        db.Add(row);return row;
    }

    private static string Correspondence(string body) => !string.IsNullOrWhiteSpace(body) && body.Length<=10000 &&
        !body.Any(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? body.Trim()
        : throw new QuoteOperationException(422,"servicing-capacity-correspondence-invalid");
}
