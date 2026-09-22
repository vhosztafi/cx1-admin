using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class ThreadService
{
    // A missing-only import produces an editable internal draft, not a sent
    // message. The caller owns the original request and same SQL transaction.
    internal async Task<OperationalMessageDraft> ImportRecordedRequest(BackOfficeDbContext db,HeldOperationalScope held,
        MatchInformationRequest request,string reference,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null||request.ActorId!=held.Actor.UserId||!held.Actor.HasCapability("message-write"))
            throw new OperationalAccessException(403,"match-request-unavailable");
        var recorded=TimeZoneInfo.ConvertTime(request.RecordedAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        return await ImportInternal(db,held,$"Information request · {reference} · {recorded:yyyy-MM-dd HH:mm}",
            $"Recorded information request ({recorded:yyyy-MM-dd}). No delivery is implied.\n\n{request.Description}",token);
    }

    internal async Task<OperationalMessageDraft> ImportInternal(BackOfficeDbContext db,HeldOperationalScope held,string title,string body,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null||!held.Actor.HasCapability("message-write"))throw new OperationalAccessException(403,"correspondence-unavailable");
        var input=new ThreadWrite("internal",title);CommunicationRules.Thread(input);
        var thread=new OperationalThread{SubjectId=held.Subjects.Single().Id,Visibility=input.Visibility,Subject=input.Subject,
            AuthorLabel=held.ActorLabel,CreatedBy=held.Actor.UserId,CreatedAt=time.GetUtcNow()};
        db.Add(thread);await db.SaveChangesAsync(token);
        var draftInput=new MessageDraftWrite(body,[],[]);
        await CommunicationScope.ValidateSelection(db,held.Actor,thread,draftInput,token);
        var draft=new OperationalMessageDraft{ThreadId=thread.Id,Body=draftInput.Body,AuthorLabel=held.ActorLabel,
            CreatedBy=held.Actor.UserId,CreatedAt=time.GetUtcNow(),UpdatedAt=time.GetUtcNow()};
        db.Add(draft);await db.SaveChangesAsync(token);await ReplaceSelections(db,draft,draftInput,held.Actor.UserId,token);return draft;
    }
}
