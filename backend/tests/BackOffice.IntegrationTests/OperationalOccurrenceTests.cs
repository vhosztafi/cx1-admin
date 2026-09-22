using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalOccurrencePinsOwnedIssuedSourceAndExclusiveExpiry()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,zone).DateTime).AddDays(1);
        f.Clock.Current=term.EndsAt.AddDays(2);
        var resolver=new IncidentOccurrenceResolver(f.Factory,f.Clock);
        var result=await resolver.Resolve(f.Underwriter,policy.Id,new(day,"Europe/London","approximate",new TimeOnly(12,30)));
        Assert.Equal("resolved",result.State);Assert.False(result.Window!.IsExact);
        Assert.All(result.Candidates,x=>Assert.Equal(version.Id,x.VersionId));Assert.Equal(Convert.ToHexStringLower(version.ContentHash),result.SourceHash);
        await Assert.ThrowsAsync<IncidentOccurrenceException>(()=>resolver.Resolve(f.Underwriter,policy.Id,new(day,"Europe/London","date"),subject:JsonSerializer.SerializeToElement(new{kind="registered-vehicle",driverId=Guid.NewGuid(),driverDeclaration="named"})));
        var exact=await resolver.Resolve(f.Underwriter,policy.Id,new(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(term.EndsAt,zone).DateTime),"Europe/London","exact",OccurredAt:term.EndsAt));
        Assert.Equal("uncovered",exact.State);Assert.Empty(exact.Candidates);
        var unknown=await resolver.Resolve(f.Underwriter,policy.Id,new(day,"Europe/London","date"),version.ProcessedAt.AddTicks(-1));
        Assert.Empty(unknown.Candidates);Assert.NotEqual("resolved",unknown.State);
        Assert.Equal("incomplete",(await resolver.Resolve(f.Underwriter,policy.Id,null)).State);
        await Assert.ThrowsAsync<IncidentOccurrenceException>(()=>resolver.Resolve(f.Underwriter,policy.Id,new(day,"Europe/London","date"),f.Clock.Current.AddDays(1)));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>resolver.Resolve(f.Underwriter with{AgencyId=policy.AgencyId},policy.Id,new(day,"Europe/London","date")));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>resolver.Resolve(f.Underwriter,Guid.NewGuid(),new(day,"Europe/London","date")));
        Assert.Equal(version.SnapshotJson,await db.Set<PolicyVersion>().Where(x=>x.Id==version.Id).Select(x=>x.SnapshotJson).SingleAsync());
    });
    [Fact]
    public Task RealSqlOperationalOccurrenceCommercialUsesRetainedLocationAndCover()=>CommercialTermsScenario(stopAfterAccepted:true,inspectAccepted:async(db,password)=>
    {
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
        var f=await CommercialIssueCommand(db,cycle,cycle.CurrentAcceptanceId!.Value,user.Id);
        await f.Service.IssueAsync(f.Actor,f.Quote.Id,f.Quote.RowVersion,f.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        using var snapshot=JsonDocument.Parse(version.SnapshotJson);var location=snapshot.RootElement.GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        var clock=new RatingClock{Current=term.EndsAt.AddDays(1)};var resolver=new IncidentOccurrenceResolver(f.Factory,clock);
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var subject=JsonSerializer.SerializeToElement(new{kind="property",locationId=location,coverCode="buildings"});
        var resolved=await resolver.Resolve(f.Actor,version.PolicyId,new(day,"Europe/London","date"),subject:subject);
        Assert.Equal("resolved",resolved.State);Assert.All(resolved.Candidates,x=>Assert.Equal(version.Id,x.VersionId));Assert.Equal(Convert.ToHexStringLower(version.ContentHash),resolved.SourceHash);
        foreach(var invalid in new[]{JsonSerializer.SerializeToElement(new{kind="property",locationId=Guid.NewGuid(),coverCode="buildings"}),JsonSerializer.SerializeToElement(new{kind="registered-vehicle",vehicleId=Guid.NewGuid()})})
            await Assert.ThrowsAsync<IncidentOccurrenceException>(()=>resolver.Resolve(f.Actor,version.PolicyId,new(day,"Europe/London","date"),subject:invalid));
    });
}
