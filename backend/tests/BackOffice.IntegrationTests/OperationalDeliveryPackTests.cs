using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalDeliveryPackRetainsSelectedVersionAcrossReplacementAndResend()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var transaction=await db.Database.BeginTransactionAsync()){await OperationalDeliverySeed.Seed(db);await transaction.CommitAsync();}
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        var contact=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.RelationshipId==policy.RelationshipId&&x.EndedAt==null);
        var commands=new SqlCommandBoundary(f.Factory,f.Clock);
        var subject=await new TaskService(f.Factory,commands,f.Clock).Register(f.Underwriter,new("policy",policy.Id),"pack-parent",default);
        var choices=new ThreadService(f.Factory,commands,f.Clock);
        var recipients=await choices.PackRecipients(f.Underwriter,subject.ResourceId,policy.RelationshipId,null,25,DateTimeOffset.UtcNow.AddDays(1),default);
        Assert.NotEmpty(recipients.Items);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>choices.PackRecipients(f.Underwriter,subject.ResourceId,Guid.NewGuid(),null,25,DateTimeOffset.UtcNow.AddDays(1),default));
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","delivery-pack-files",db.Database.GetDbConnection().Database)),[]);
        var files=new FileService(f.Factory,commands,store,f.Clock);
        var documents=new DocumentService(f.Factory,commands,new PolicyDocumentRenderService(f.Factory,new PolicyDocumentRenderer()),f.Clock,files);
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jN1sAAAAASUVORK5CYII=");
        var upload=await files.Upload(f.Underwriter,subject.ResourceId,"original.png","image/png",new MemoryStream(bytes),"pack-upload",default);
        var input=new DocumentUploadInput("evidence",upload.ResourceId,"agency","Fictional pack evidence",RelationshipId:policy.RelationshipId);
        var attached=await documents.AttachUpload(f.Underwriter,subject.ResourceId,input,"pack-attach",default);
        var sender=new MessageDeliveryService(commands,f.Clock);var packs=new DocumentPackService(commands,sender);
        var pack=new DocumentPackWrite([attached.ResourceId],[contact.Id],"Fictional document pack","Selected original evidence");
        await Assert.ThrowsAsync<OperationalAccessException>(()=>packs.Send(f.Underwriter,subject.ResourceId,pack,"pack-pending",default));
        Assert.Empty(await db.Set<OperationalDelivery>().ToArrayAsync());
        Assert.True(await new FileFinalizationWorker(f.Factory,store,f.Clock).Process(upload.ResourceId,default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>packs.Send(f.Underwriter,subject.ResourceId,pack with{RecipientContactIds=[Guid.NewGuid()]},"pack-foreign-recipient",default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>packs.Send(f.Underwriter,subject.ResourceId,pack with{DocumentVersionIds=[Guid.NewGuid()]},"pack-foreign-version",default));
        var internalUpload=await files.Upload(f.Underwriter,subject.ResourceId,"internal.png","image/png",new MemoryStream(bytes),"pack-internal-upload",default);
        var internalVersion=await documents.AttachUpload(f.Underwriter,subject.ResourceId,new("evidence",internalUpload.ResourceId,"internal","Internal evidence"),"pack-internal-attach",default);
        Assert.True(await new FileFinalizationWorker(f.Factory,store,f.Clock).Process(internalUpload.ResourceId,default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>packs.Send(f.Underwriter,subject.ResourceId,pack with{DocumentVersionIds=[internalVersion.ResourceId]},"pack-internal-version",default));
        var queued=await packs.Send(f.Underwriter,subject.ResourceId,pack,"pack-send",default);
        Assert.True((await packs.Send(f.Underwriter,subject.ResourceId,pack,"pack-send",default)).Replayed);
        await Assert.ThrowsAsync<CommandKeyConflictException>(()=>packs.Send(f.Underwriter,subject.ResourceId,pack with{Body="Changed body"},"pack-send",default));
        var original=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync();
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var replacementUpload=await files.Upload(f.Underwriter,subject.ResourceId,"replacement.png","image/png",new MemoryStream(bytes),"pack-replacement-upload",default);
        var replacement=await documents.AttachUpload(f.Underwriter,subject.ResourceId,input with{UploadId=replacementUpload.ResourceId,DocumentId=version.DocumentId},"pack-replacement-attach",default);
        Assert.True(await new FileFinalizationWorker(f.Factory,store,f.Clock).Process(replacementUpload.ResourceId,default));
        Assert.NotEqual(attached.ResourceId,replacement.ResourceId);
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new MessageDeliveryWorker(f.Factory,files,f.Clock);
        var lease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,queued.ResourceId))!;
        var effect=await worker.ExecuteProvider(lease);Assert.NotNull(effect);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,effect));
        var first=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync();
        var resent=await sender.Recover(f.Underwriter,first.Id,TaskService.Etag(first.RowVersion),"Fictional requested second copy",true,false,"pack-resend",default);
        var second=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.WorkId==resent.ResourceId);
        Assert.Equal(original.ContentJson,second.ContentJson);Assert.Equal(original.ContentHash,second.ContentHash);
        Assert.Equal(first.Id,second.ResendOfId);Assert.NotEqual(first.WorkId,second.WorkId);
        var attachments=await db.Set<OperationalDeliveryAttachment>().AsNoTracking().ToArrayAsync();
        Assert.Equal(2,attachments.Length);Assert.All(attachments,x=>Assert.Equal(attached.ResourceId,x.DocumentVersionId));
        Assert.All(attachments,x=>Assert.Equal("original.png",x.OriginalName));
        Assert.Equal("delivered",await db.Set<OperationalDelivery>().Where(x=>x.Id==first.Id).Select(x=>x.State).SingleAsync());
        var secondLease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,resent.ResourceId))!;
        var secondEffect=await worker.ExecuteProvider(secondLease);Assert.NotNull(secondEffect);Assert.Equal(InboxApplication.Applied,await worker.Apply(secondLease,secondEffect));
        Assert.Equal(2,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
        var corruptQueued=await packs.Send(f.Underwriter,subject.ResourceId,pack,"pack-corrupt-bytes",default);
        var corruptLease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,corruptQueued.ResourceId))!;
        var fileId=attachments[0].FileObjectId;
        var ownedFile=Path.GetFullPath(Path.Combine(".local","delivery-pack-files",db.Database.GetDbConnection().Database,"ready",fileId.ToString("N")+".bin"));
        var changedBytes=bytes.ToArray();changedBytes[^1]^=1;await File.WriteAllBytesAsync(ownedFile,changedBytes);
        Assert.Equal(JobFailure.InvalidPayload,(await Assert.ThrowsAsync<MessageDeliveryException>(()=>worker.ExecuteProvider(corruptLease))).Failure);
        Assert.True(await leases.FailAsync(corruptLease,JobFailure.InvalidPayload));
        Assert.Equal(2,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalDeliveryAttachment SET OriginalName=N'changed.png' WHERE DeliveryId={first.Id}"))).Number);
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalDelivery SET ContentJson=N'{{}}' WHERE Id={first.Id}"))).Number);
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260922083328_OperationalCommunication"))).Number);
        Assert.Equal(3,await db.Set<OperationalDelivery>().CountAsync());
    });
}
