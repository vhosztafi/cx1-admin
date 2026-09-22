using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal static class CommunicationScope
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    internal static OperationalAccessException Missing()=>new(404,"communication-not-found");
    internal static OperationalAccessException BadCursor()=>new(400,"invalid-communication-cursor");
    internal static void Page(int size){if(size is <1 or >100)throw new OperationalAccessException(400,"invalid-communication-page");}
    internal static async Task<(OperationalThread Thread,HeldOperationalScope Held)> HoldThread(BackOfficeDbContext db,ActorContext actor,Guid threadId,string capability,CancellationToken token)
    {
        var hint=await db.Set<OperationalThread>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==threadId,token)??throw Missing();
        var held=await OperationalScope.HoldSubjects(db,actor,[hint.SubjectId],capability,token);
        await Audience(db,held.Subjects.Single(),hint.RelationshipId,token);
        var row=await db.Set<OperationalThread>().FromSqlInterpolated($"SELECT * FROM OperationalThread WITH(HOLDLOCK,ROWLOCK) WHERE Id={threadId}").AsNoTracking().SingleOrDefaultAsync(token)??throw Missing();
        if(row.SubjectId!=hint.SubjectId||row.RelationshipId!=hint.RelationshipId||row.Visibility!=hint.Visibility)throw Missing();
        return(row,held);
    }
    internal static async Task Audience(BackOfficeDbContext db,OperationalSubject subject,Guid? relationshipId,CancellationToken token)
    {
        if(relationshipId is not Guid id)return;
        if(!await DocumentService.AllowedAudience(db,subject).AnyAsync(x=>x.Id==id,token))throw Missing();
        var row=await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}").AsNoTracking().SingleAsync(token);
        if(row.State!="active")throw Missing();
    }
    internal static async Task ValidateSelection(BackOfficeDbContext db,ActorContext actor,OperationalThread thread,MessageDraftWrite input,CancellationToken token)
    {
        CommunicationRules.Draft(input,thread.Visibility);
        foreach(var id in input.RecipientContactIds.Order())
        {
            var contact=await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}").AsNoTracking().SingleOrDefaultAsync(token);
            if(contact is null||contact.RelationshipId!=thread.RelationshipId||contact.EndedAt is not null||!CommunicationRules.Email(contact.Email))throw Missing();
        }
        foreach(var id in input.AttachmentVersionIds.Order())
        {
            var document=await(from v in db.Set<DocumentVersion>() join d in db.Set<OperationalDocument>() on v.DocumentId equals d.Id where v.Id==id select d).AsNoTracking().SingleOrDefaultAsync(token);
            if(document is null||document.SubjectId!=thread.SubjectId||thread.Visibility=="agency"&&(document.Visibility!="agency"||document.RelationshipId!=thread.RelationshipId))throw Missing();
            await DocumentService.AttachmentVersion(db,actor,thread.SubjectId,id,true,token);
        }
    }
}
