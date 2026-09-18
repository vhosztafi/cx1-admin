using BackOffice.Api;
using BackOffice.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class PartyPagingTests
{
    [Fact]
    public void GuidKeysetRetainsScopeVersionSizeAndFilterBindings()
    {
        var time=new Clock();var paging=new PartyPaging(new EphemeralDataProtectionProvider(),time);
        var actor=new ActorContext(Guid.NewGuid(),Guid.NewGuid(),null,new HashSet<string>{"underwriter"});
        var context=Context();context.Request.QueryString=new QueryString("?pageSize=1&cycleId=owned");
        var page=paging.ReadBound(context,actor,"servicing-files-created-id","v1","cycleId")!;
        var id=Guid.NewGuid();var cursor=paging.NextGuid(page,id)!;
        context.Request.QueryString=new QueryString("?pageSize=1&cycleId=owned&cursor="+Uri.EscapeDataString(cursor));
        Assert.Equal(id,paging.ReadBound(context,actor,"servicing-files-created-id","v1","cycleId")!.KeyId);
        Assert.Null(paging.ReadBound(context,actor,"servicing-files-created-id","v2","cycleId"));
        Assert.Null(paging.ReadBound(context,actor,"servicing-events-sequence","v1","cycleId"));
        Assert.Null(paging.NextGuid(page,Guid.Empty));Assert.Null(paging.NextGuid(page,null));
        foreach(var query in new[]{"?pageSize=2&cycleId=owned","?pageSize=1&cycleId=foreign","?pageSize=1&cycleId=owned&unknown=1"})
        {
            context.Request.QueryString=new QueryString(query+"&cursor="+Uri.EscapeDataString(cursor));
            Assert.Null(paging.ReadBound(context,actor,"servicing-files-created-id","v1","cycleId"));
        }
    }
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
