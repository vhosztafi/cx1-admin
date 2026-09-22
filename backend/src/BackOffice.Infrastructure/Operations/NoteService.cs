using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record CommunicationPage(IReadOnlyList<object> Items,int TotalCount,Guid? NextId);
public sealed class NoteService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Add(ActorContext actor,Guid subjectId,string body,string key,CancellationToken token)
    {
        CommunicationRules.Note(body);HeldOperationalScope? held=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/records/{subjectId}/notes",key,Guid.NewGuid()),new{body},"communication.note-added",
            async(db,ct)=>held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"internal-note-write",ct),
            async(db,ct)=>
            {
                var row=new InternalNote{SubjectId=subjectId,Body=body,AuthorLabel=held!.ActorLabel,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};
                db.Add(row);await db.SaveChangesAsync(ct);
                return new(row.Id,201,JsonSerializer.Serialize(View(row),CommunicationScope.Json));
            },token);
    }
    public async Task<CommunicationPage> List(ActorContext actor,Guid subjectId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        await OperationalScope.HoldSubjects(db,actor,[subjectId],"internal-note-read",token);
        var query=db.Set<InternalNote>().AsNoTracking().Where(x=>x.SubjectId==subjectId&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid id)
        {
            var cursor=await query.SingleOrDefaultAsync(x=>x.Id==id,token)??throw CommunicationScope.BadCursor();
            query=query.Where(x=>x.CreatedAt<cursor.CreatedAt||x.CreatedAt==cursor.CreatedAt&&x.Id.CompareTo(id)<0);
        }
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);
        await transaction.CommitAsync(token);return new(rows.Take(size).Select(View).ToArray(),total,rows.Length>size?rows[size-1].Id:null);
    }
    private static object View(InternalNote row)=>new{id=row.Id,subjectRecordId=row.SubjectId,row.Body,row.AuthorLabel,row.CreatedAt};
}
