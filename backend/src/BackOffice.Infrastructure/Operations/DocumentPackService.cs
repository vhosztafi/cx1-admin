using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed class DocumentPackService(SqlCommandBoundary commands,MessageDeliveryService delivery)
{
    public async Task<CommandOutcome> Send(ActorContext actor,Guid subjectId,DocumentPackWrite input,string key,CancellationToken token)
    {
        OperationalDeliveryRules.Pack(input);DeliveryContent? content=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/records/{subjectId}/document-deliveries",key,Guid.NewGuid()),input,"documents.pack-queued",
            async(db,ct)=>
            {
                var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-send",ct);
                var relationship=await db.Set<Contact>().Where(x=>x.Id==input.RecipientContactIds[0]).Select(x=>(Guid?)x.RelationshipId).SingleOrDefaultAsync(ct)??throw CommunicationScope.Missing();
                content=await DeliverySnapshots.Capture(db,actor,held.Subjects.Single(),relationship,input.Subject,new(input.Body,input.RecipientContactIds,input.DocumentVersionIds),ct);
            },(db,ct)=>delivery.Queue(db,actor,content!,null,null,ct),token);
    }
}
