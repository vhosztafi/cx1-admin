using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal sealed record DeliveryRecipient(Guid ContactId,string Name,string Email);
internal sealed record DeliveryFile(Guid VersionId,Guid FileId,string Hash,string Name,string MediaType,long Length);
internal sealed record DeliveryContent(string Format,Guid SubjectId,Guid RelationshipId,string Subject,string Body,DeliveryRecipient[] Recipients,DeliveryFile[] Attachments);
internal static class DeliverySnapshots
{
    internal static string Serialize(DeliveryContent content)=>JsonSerializer.Serialize(content,CommunicationScope.Json);
    internal static string Hash(string json)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    internal static async Task<DeliveryContent> Capture(BackOfficeDbContext db,ActorContext actor,OperationalSubject subject,Guid relationshipId,string title,MessageDraftWrite input,CancellationToken token)
    {
        OperationalDeliveryRules.Message("agency",title,input);
        await CommunicationScope.Audience(db,subject,relationshipId,token);
        await CommunicationScope.ValidateSelection(db,actor,new(){SubjectId=subject.Id,Visibility="agency",RelationshipId=relationshipId},input,token);
        var recipients=new List<DeliveryRecipient>();
        foreach(var id in input.RecipientContactIds.Order())
        {
            var row=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.Id==id,token);
            recipients.Add(new(row.Id,row.DeclaredFullName,row.Email!));
        }
        var attachments=new List<DeliveryFile>();
        foreach(var id in input.AttachmentVersionIds.Order())
        {
            var row=await(from v in db.Set<DocumentVersion>() join c in db.Set<DocumentVersionContent>() on v.Id equals c.VersionId
                join f in db.Set<FileObject>() on c.FileObjectId equals f.Id where v.Id==id
                select new{v.Id,FileId=f.Id,f.Sha256,v.OriginalName,f.MediaType,f.ByteLength}).SingleAsync(token);
            attachments.Add(new(row.Id,row.FileId,row.Sha256,row.OriginalName,row.MediaType,row.ByteLength));
        }
        return new("operational-delivery-1",subject.Id,relationshipId,title,input.Body,recipients.ToArray(),attachments.ToArray());
    }

    internal static async Task UpdateMessageState(BackOfficeDbContext db,OperationalDelivery delivery,CancellationToken token)
    {
        if(delivery.MessageVersionId is not Guid versionId)return;
        var messageId=await db.Set<OperationalMessageVersion>().Where(x=>x.Id==versionId).Select(x=>x.MessageId).SingleAsync(token);
        var states=await db.Set<OperationalDelivery>().Where(x=>x.MessageVersionId==versionId&&x.Id!=delivery.Id).Select(x=>x.State).ToListAsync(token);states.Add(delivery.State);
        var message=await db.Set<OperationalMessageDraft>().SingleAsync(x=>x.Id==messageId,token);
        message.State=states.Contains("delivered")?"sent":states.Contains("queued")?"queued":states.Contains("failed")?"failed":"superseded";
    }
}
