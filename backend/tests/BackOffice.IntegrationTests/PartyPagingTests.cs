using BackOffice.Api;
using BackOffice.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class PartyPagingTests
{
    [Fact]
    public void CursorRejectsTamperingExpiryAndChangedScopeOrOrdering()
    {
        var time=new Clock();var paging=new PartyPaging(new EphemeralDataProtectionProvider(),time);
        var actor=new ActorContext(Guid.NewGuid(),Guid.NewGuid(),null,new HashSet<string>{"servicing","underwriter"});
        var context=Context();var page=paging.Read(context,actor,"reference,id","q")!;
        var cursor=paging.Next(page,true)!;context.Request.QueryString=new QueryString("?cursor="+Uri.EscapeDataString(cursor));
        Assert.Equal(25,paging.Read(context,actor,"reference,id","q")!.Offset);
        Assert.NotNull(paging.Read(context,actor with {Roles=new HashSet<string>{"underwriter","servicing"}},"reference,id","q"));
        Assert.Null(paging.Read(context,actor with {AgencyId=Guid.NewGuid()},"reference,id","q"));
        Assert.Null(paging.Read(context,actor with {TeamId=Guid.NewGuid()},"reference,id","q"));
        Assert.Null(paging.Read(context,actor with {UserId=Guid.NewGuid()},"reference,id","q"));
        Assert.Null(paging.Read(context,actor,"name,id","q"));
        context.Request.Path="/api/v1/relationship-agencies";Assert.Null(paging.Read(context,actor,"reference,id","q"));
        context.Request.Path="/api/v1/clients";time.Now=time.Now.AddMinutes(16);Assert.Null(paging.Read(context,actor,"reference,id","q"));
        context.Request.QueryString=new QueryString("?cursor=broken");Assert.Null(paging.Read(context,actor,"reference,id","q"));
    }
    private static DefaultHttpContext Context(){var context=new DefaultHttpContext();context.Request.Path="/api/v1/clients";return context;}
    private sealed class Clock:TimeProvider {public DateTimeOffset Now{get;set;}=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
}
