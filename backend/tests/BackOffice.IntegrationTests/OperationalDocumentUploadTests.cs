using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalDocumentUploadTests
{
    [Fact]
    public async Task RealSqlOperationalDocumentUploadBindsOneVersionAndPreservesOriginalBytes()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,x=>x.UseCompatibilityLevel(160)).Options;
        var root=Path.GetFullPath(Path.Combine(".local","document-upload",owned));
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();
            var user=new StaffUser{Email="document-upload@cover.example",NormalizedEmail="DOCUMENT-UPLOAD@COVER.EXAMPLE",DisplayName="Document upload actor"};
            var role=new Role{Code="agency-admin",Scope="internal"};var agency=new Agency{Reference="AG-DOC-UPLOAD",LegalName="Fictional upload agency"};
            var other=new Agency{Reference="AG-DOC-OTHER",LegalName="Other fictional agency"};
            db.AddRange(user,role,agency,other);await db.SaveChangesAsync();db.Add(new UserRole{UserId=user.Id,RoleId=role.Id});await db.SaveChangesAsync();
            var actor=new ActorContext(user.Id,null,null,new HashSet<string>{"agency-admin"});var factory=new Factory(options);var clock=TimeProvider.System;
            var commands=new SqlCommandBoundary(factory,clock);var tasks=new TaskService(factory,commands,clock);
            var subject=await tasks.Register(actor,new("agency",agency.Id),"upload-subject",default);
            var foreign=await tasks.Register(actor,new("agency",other.Id),"foreign-subject",default);
            var store=new OperationalFileStore(root,[]);var files=new FileService(factory,commands,store,clock);
            var documents=new DocumentService(factory,commands,new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer()),clock,files);
            var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jN1sAAAAASUVORK5CYII=");
            var uploaded=await files.Upload(actor,subject.ResourceId,"evidence.png","image/png",new MemoryStream(bytes),"upload-png",default);
            var input=new DocumentUploadInput("evidence",uploaded.ResourceId,"internal","Attach fictional supplied evidence");
            var attached=await documents.AttachUpload(actor,subject.ResourceId,input,"attach-upload",default);
            Assert.True((await documents.AttachUpload(actor,subject.ResourceId,input,"attach-upload",default)).Replayed);
            Assert.Equal(attached.ResourceId,(await documents.AttachUpload(actor,subject.ResourceId,input,"attach-other-key",default)).ResourceId);
            Assert.Equal(409,(await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.AttachUpload(actor,subject.ResourceId,input with{Kind="statement-of-fact"},"conflicting-upload-kind",default))).Status);
            var storageId=await db.Set<FileObject>().Where(x=>x.WorkId==uploaded.ResourceId).Select(x=>x.Id).SingleAsync();
            Assert.Equal(404,(await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.AttachUpload(actor,subject.ResourceId,input with{UploadId=storageId},"storage-id-is-not-upload",default))).Status);
            var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync();Assert.Equal("upload",version.SourceKind);Assert.Equal(uploaded.ResourceId,version.WorkId);
            Assert.Null(version.TemplateVersionId);Assert.Null(version.SourceHash);
            Assert.Equal(409,(await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.DownloadVersion(actor,version.Id,default))).Status);
            Assert.True(await new FileFinalizationWorker(factory,store,clock).Process(uploaded.ResourceId,default));
            using(var view=JsonDocument.Parse((await documents.ReadVersion(actor,version.Id,default)).Body))
            {Assert.Equal("ready",view.RootElement.GetProperty("state").GetString());Assert.Equal("image/png",view.RootElement.GetProperty("contentType").GetString());}
            await using(var downloaded=await documents.DownloadVersion(actor,version.Id,default))
            {using var memory=new MemoryStream();await downloaded.Content.CopyToAsync(memory);Assert.Equal(bytes,memory.ToArray());}
            var foreignUpload=await files.Upload(actor,foreign.ResourceId,"other.png","image/png",new MemoryStream(bytes),"foreign-upload",default);
            await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.AttachUpload(actor,subject.ResourceId,input with{UploadId=foreignUpload.ResourceId},"attach-foreign",default));
            Assert.Equal(1,await db.Set<DocumentVersion>().CountAsync());Assert.Equal(1,await db.Set<DocumentVersionContent>().CountAsync());
            var replacement=await files.Upload(actor,subject.ResourceId,"replacement.png","image/png",new MemoryStream(bytes),"replacement-upload",default);
            var replaced=await documents.AttachUpload(actor,subject.ResourceId,input with{UploadId=replacement.ResourceId,DocumentId=version.DocumentId},"replace-with-new-version",default);
            var second=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==replaced.ResourceId);
            Assert.Equal(2,second.Number);Assert.Equal(version.DocumentId,second.DocumentId);Assert.NotEqual(version.WorkId,second.WorkId);
            Assert.Equal(2,await db.Set<DocumentVersionContent>().CountAsync());
            await using(var retained=await documents.DownloadVersion(actor,version.Id,default))
            {using var memory=new MemoryStream();await retained.Content.CopyToAsync(memory);Assert.Equal(bytes,memory.ToArray());}
            Assert.Empty(await db.Set<OutboxWork>().Where(x=>x.Kind=="document-generation").ToArrayAsync());
            user.State="suspended";await db.SaveChangesAsync();
            await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.AttachUpload(actor,subject.ResourceId,input,"attach-upload",default));
        }
        finally
        {
            if(connection.InitialCatalog!=owned || !owned.StartsWith("CoverMGA_Test_",StringComparison.Ordinal))throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options):IDbContextFactory<BackOfficeDbContext>
    { public BackOfficeDbContext CreateDbContext()=>new(options); }
}
