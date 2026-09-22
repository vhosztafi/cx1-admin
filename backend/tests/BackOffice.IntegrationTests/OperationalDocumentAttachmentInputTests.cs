using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalDocumentAttachmentInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitNullReasonIsRejectedBeforeAnAttachmentWrite(bool remove)
    {
        // JsonRequired requires presence, not a non-null string. The service
        // must reject explicit JSON null instead of reaching SQL constraints.
        var service=new TaskService(null!,null!,TimeProvider.System);
        var actor=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string>{"underwriter"});
        var error=await Assert.ThrowsAsync<TaskRuleException>(async()=>
        {
            if(remove)await service.RemoveDocumentAttachment(actor,Guid.NewGuid(),"\"AAAAAAAAAAA=\"","null-reason",Guid.NewGuid(),null!,default);
            else await service.AttachDocument(actor,Guid.NewGuid(),"\"AAAAAAAAAAA=\"","null-reason",Guid.NewGuid(),null!,default);
        });
        Assert.Equal("task-reason-required",error.Code);
    }
}
