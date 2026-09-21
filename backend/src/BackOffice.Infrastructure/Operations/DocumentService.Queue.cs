using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    public async Task<IReadOnlyList<Guid>> UnregisteredCancellationNotices(int offset,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        return await db.Set<CancellationConsequence>().Where(n=>n.Kind=="notice"&&n.CreatedBy!=null&&
            !db.Set<DocumentVersion>().Any(v=>v.CancellationConsequenceId==n.Id))
            .OrderBy(n=>n.CreatedAt).ThenBy(n=>n.Id).Skip(offset).Take(32).Select(n=>n.Id).ToArrayAsync(token);
    }

    public async Task RegisterOriginalCancellationNotice(Guid noticeId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(n=>n.Id==noticeId,token);
        var user=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(u=>u.Id==notice.CreatedBy,token)
            ??throw new OperationalAccessException(403,"document-originator-unavailable");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id
            where link.UserId==user.Id select role.Code).ToArrayAsync(token);
        var actor=new ActorContext(user.Id,user.TeamId,user.AgencyId,roles.ToHashSet(StringComparer.Ordinal));
        await RegisterCancellationNotice(actor,noticeId,"original-cancellation-document/"+noticeId.ToString("N"),token);
    }

    public async Task<IReadOnlyList<Guid>> UnregisteredRequests(int offset,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);var now=time.GetUtcNow();
        return await(from request in db.Set<PolicyDocumentRequest>() join work in db.Set<OutboxWork>() on request.WorkId equals work.Id
            where request.CreatedBy!=null&&work.Kind=="policy-document"&&work.State=="pending"&&work.NextAttemptAt<=now&&
                !db.Set<DocumentVersion>().Any(v=>v.PolicyDocumentRequestId==request.Id)
            orderby request.CreatedAt,request.Id select request.Id).Skip(offset).Take(32).ToArrayAsync(token);
    }

    public async Task RegisterOriginalRequest(Guid requestId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==requestId,token);
        var user=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.CreatedBy,token)
            ??throw new OperationalAccessException(403,"document-originator-unavailable");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code).ToArrayAsync(token);
        var actor=new ActorContext(user.Id,user.TeamId,user.AgencyId,roles.ToHashSet(StringComparer.Ordinal));
        try{await RegisterRetainedRequest(actor,requestId,"original-document/"+requestId.ToString("N"),token);}
        catch(DocumentRenderException invalid)
        {
            await using var transaction=await db.Database.BeginTransactionAsync(token);
            await OperationalScope.HoldParents(db,actor,[new("policy",request.PolicyId)],"document-generate",token);
            var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={request.WorkId}").SingleAsync(token);
            if(work.State=="pending"&&work.Attempts<work.AttemptLimit)
            {
                var now=time.GetUtcNow();work.Attempts++;work.State="failed";work.CompletedAt=now;work.ErrorCode=invalid.Code;
                db.Add(new AdapterAttempt{WorkId=work.Id,AttemptNumber=work.Attempts,StartedAt=now,EndedAt=now,Outcome="rejected",ErrorCode=invalid.Code,
                    Request=JsonSerializer.Serialize(new{requestId}),CreatedBy=user.Id,CreatedAt=now});
                db.Add(new JobException{WorkId=work.Id,Code=invalid.Code,OccurredAt=now});await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
        }
    }

    public async Task<IReadOnlyList<Guid>> PendingGenerations(int offset,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);var now=time.GetUtcNow();
        return await(from version in db.Set<DocumentVersion>() join work in db.Set<OutboxWork>() on version.WorkId equals work.Id
            where (version.PolicyDocumentRequestId!=null||work.Kind=="document-generation")&&work.State=="pending"&&work.NextAttemptAt<=now
            orderby work.NextAttemptAt,work.Id select work.Id).Skip(offset).Take(32).ToArrayAsync(token);
    }
}
