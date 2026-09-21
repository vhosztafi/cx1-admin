using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public enum DocumentGenerationFault { AfterRender, AfterContentCommit, AfterFinalize }

public sealed class DocumentGenerationWorker(IDbContextFactory<BackOfficeDbContext> factory, PolicyDocumentRenderService sources,
    IOperationalFileStore store, TimeProvider time, Action<DocumentGenerationFault>? fault = null)
{
    public async Task<bool> Process(Guid workId,CancellationToken token)
    {
        var actor=await Originator(workId,token);
        DocumentVersion version; Guid? fileId;
        await using(var db=await factory.CreateDbContextAsync(token))
        await using(var transaction=await db.Database.BeginTransactionAsync(token))
        {
            version=await HoldVersion(db,actor,workId,token);
            var work=await HoldWork(db,version,token);
            if(work.State!="pending"||work.NextAttemptAt>time.GetUtcNow()){await transaction.CommitAsync(token);return false;}
            fileId=await db.Set<DocumentVersionContent>().Where(x=>x.VersionId==version.Id).Select(x=>(Guid?)x.FileObjectId).SingleOrDefaultAsync(token);
            await transaction.CommitAsync(token);
        }
        if(fileId is null)
        {
            RenderedPolicyDocument rendered;
            StagedOperationalFile staged;
            try
            {
                if(version.PolicyDocumentRequestId is not Guid requestId) throw new DocumentRenderException("document-source-not-supported");
                var input=await sources.LoadRetainedRequest(actor,requestId,token);
                if(input.SourceHash!=version.SourceHash||input.TemplateHash!=version.TemplateHash||input.TemplateId!=version.TemplateVersionId||input.SourceId!=version.PolicyVersionId)
                    throw new DocumentRenderException("document-version-source-mismatch");
                rendered=await sources.RenderRetainedRequest(actor,requestId,token);
                staged=await store.Stage(version.OriginalName,"application/pdf",new MemoryStream(rendered.Bytes,writable:false),FileRules.MaximumFileBytes,token);
                if(staged.Sha256!=rendered.Sha256)throw new DocumentRenderException("document-rendered-file-mismatch");
            }
            catch(Exception error) when(error is DocumentRenderException or FileRuleException or IOException or UnauthorizedAccessException)
            {await Failure(actor,workId,error is DocumentRenderException invalid?invalid.Code:"document-rendering-unavailable",error is DocumentRenderException or FileRuleException,token);return true;}
            fault?.Invoke(DocumentGenerationFault.AfterRender);
            await using(var db=await factory.CreateDbContextAsync(token))
            await using(var transaction=await db.Database.BeginTransactionAsync(token))
            {
                version=await HoldVersion(db,actor,workId,token);
                await DocumentService.Lock(db,"CoverMGA.File."+staged.Id.ToString("N"),token);
                var work=await HoldWork(db,version,token);
                if(work.State!="pending"){await transaction.CommitAsync(token);return false;}
                fileId=await db.Set<DocumentVersionContent>().Where(x=>x.VersionId==version.Id).Select(x=>(Guid?)x.FileObjectId).SingleOrDefaultAsync(token);
                if(fileId is null)
                {
                    var document=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId,token);
                    var now=time.GetUtcNow();
                    var finalize=new OutboxWork{Kind="file-finalization",SubjectRecordId=document.SubjectId,OperationKey="file/"+staged.Id.ToString("N"),
                        Payload=JsonSerializer.Serialize(new{fileId=staged.Id}),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now,NextAttemptAt=now};
                    var file=new FileObject{Id=staged.Id,SubjectId=document.SubjectId,WorkId=finalize.Id,FileName=version.OriginalName,MediaType="application/pdf",
                        ByteLength=staged.Description.ByteLength,Sha256=staged.Sha256,CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                    var binding=new DocumentVersionContent{VersionId=version.Id,FileObjectId=file.Id,PageCount=rendered.PageCount,RendererVersion=rendered.RendererVersion,
                        ProjectionVersion=rendered.ProjectionVersion,FontVersion=rendered.FontVersion,CreatedBy=actor.UserId,CreatedAt=now};
                    db.AddRange(finalize,file,binding);await db.SaveChangesAsync(token);fileId=file.Id;
                }
                await transaction.CommitAsync(token);
            }
            fault?.Invoke(DocumentGenerationFault.AfterContentCommit);
        }
        await using(var read=await factory.CreateDbContextAsync(token))
        {
            var finalizeId=await read.Set<FileObject>().Where(x=>x.Id==fileId).Select(x=>x.WorkId).SingleAsync(token)
                ??throw new DocumentRenderException("document-file-work-missing");
            await new FileFinalizationWorker(factory,store,time).Process(finalizeId,token);
        }
        fault?.Invoke(DocumentGenerationFault.AfterFinalize);
        await using(var db=await factory.CreateDbContextAsync(token))
        await using(var transaction=await db.Database.BeginTransactionAsync(token))
        {
            version=await HoldVersion(db,actor,workId,token);
            var file=await db.Set<FileObject>().FromSqlInterpolated($"SELECT * FROM FileObject WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={fileId}").SingleAsync(token);
            var work=await HoldWork(db,version,token);
            if(work.State!="pending"){await transaction.CommitAsync(token);return false;}
            if(file.State=="pending"){await transaction.CommitAsync(token);return true;}
            var now=time.GetUtcNow();
            var succeeded=file.State=="ready";
            if(succeeded)
            {
                try { await using var verified=await store.OpenReady(file.Id,file.ByteLength,file.Sha256,token); }
                catch(OperationalFileStoreException error) when(error.Code is "file-content-mismatch" or "file-path-invalid" or "file-bytes-missing")
                { file.State="quarantined";file.FailureCode=error.Code;succeeded=false; }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {
                    await transaction.RollbackAsync(token);
                    await Failure(actor,workId,"document-file-unavailable",false,token);return true;
                }
            }
            work.Attempts++;
            work.State=succeeded?"succeeded":"failed";work.CompletedAt=now;work.ErrorCode=succeeded?null:"document-file-quarantined";
            work.Result=JsonSerializer.Serialize(new{documentVersionId=version.Id,state=succeeded?"ready":"quarantined"});
            db.Add(new AdapterAttempt{WorkId=work.Id,AttemptNumber=work.Attempts,StartedAt=now,EndedAt=now,Outcome=succeeded?"succeeded":"rejected",
                ErrorCode=work.ErrorCode,Request=JsonSerializer.Serialize(new{documentVersionId=version.Id}),CreatedBy=actor.UserId,CreatedAt=now});
            if(!succeeded)db.Add(new JobException{WorkId=work.Id,Code=work.ErrorCode!,OccurredAt=now});
            await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return true;
        }
    }

    private async Task Failure(ActorContext actor,Guid workId,string code,bool permanent,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var version=await HoldVersion(db,actor,workId,token);var work=await HoldWork(db,version,token);
        if(work.State!="pending"){await transaction.CommitAsync(token);return;}
        var now=time.GetUtcNow();work.Attempts++;var terminal=permanent||work.Attempts>=work.AttemptLimit;
        work.ErrorCode=code;work.NextAttemptAt=now.AddMinutes(1);
        db.Add(new AdapterAttempt{WorkId=workId,AttemptNumber=work.Attempts,StartedAt=now,EndedAt=now,Outcome=terminal?"rejected":"transient-failure",
            ErrorCode=code,Request=JsonSerializer.Serialize(new{documentVersionId=version.Id}),CreatedBy=actor.UserId,CreatedAt=now});
        if(terminal){work.State="failed";work.CompletedAt=now;db.Add(new JobException{WorkId=work.Id,Code=code,OccurredAt=now});}
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);
    }

    private async Task<ActorContext> Originator(Guid workId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var id=await db.Set<DocumentVersion>().Where(x=>x.WorkId==workId).Select(x=>x.CreatedBy).SingleOrDefaultAsync(token)
            ??throw new OperationalAccessException(404,"document-work-not-found");
        var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==id,token);
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==id select role.Code).ToArrayAsync(token);
        return new(user.Id,user.TeamId,user.AgencyId,roles.ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<DocumentVersion> HoldVersion(BackOfficeDbContext db,ActorContext actor,Guid workId,CancellationToken token)
    {
        var hint=await(from version in db.Set<DocumentVersion>() join document in db.Set<OperationalDocument>() on version.DocumentId equals document.Id
            where version.WorkId==workId select new{version.Id,document.SubjectId,version.CreatedBy}).SingleOrDefaultAsync(token)
            ??throw new OperationalAccessException(404,"document-work-not-found");
        await OperationalScope.HoldSubjects(db,actor,[hint.SubjectId],"document-generate",token);
        if(hint.CreatedBy!=actor.UserId)throw new OperationalAccessException(403,"document-originator-required");
        await DocumentService.Lock(db,"CoverMGA.DocumentVersion."+hint.Id.ToString("N"),token);
        return await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==hint.Id,token);
    }

    private static async Task<OutboxWork> HoldWork(BackOfficeDbContext db,DocumentVersion version,CancellationToken token)
    {
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={version.WorkId}").SingleAsync(token);
        if(work.Kind!="policy-document"||version.PolicyDocumentRequestId is null||work.SubjectRecordId!=version.PolicyDocumentRequestId||work.OperationKey!="policy-document/"+version.PolicyDocumentRequestId.Value.ToString("N"))
            throw new DocumentRenderException("document-work-mismatch");
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==version.PolicyDocumentRequestId,token);
        if(work.Payload!=request.PayloadJson||request.WorkId!=work.Id)throw new DocumentRenderException("document-work-payload-mismatch");
        if(work.State=="pending"&&work.Attempts>=work.AttemptLimit)throw new DocumentRenderException("document-retry-budget-invalid");
        return work;
    }
}
