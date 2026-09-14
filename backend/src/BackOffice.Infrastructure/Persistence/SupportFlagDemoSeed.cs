using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class SupportFlagDemoSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Support demo seed requires the shared seed transaction.");
        var client=PartyDemoSeed.ClientId(3);var origin=PartyDemoSeed.RelationshipId(3,1);var target=PartyDemoSeed.RelationshipId(3,2);var person=ContactDemoSeed.PersonId(1);
        foreach(var id in new[] {origin,target}.Order())
            if(await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM [ClientAgencyRelationship] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(token) is null)return;
        // Existing flags, including ended flags, belong to the demo user and are never replaced.
        if(await db.Set<SupportFlag>().AnyAsync(x=>x.ClientId==client,token))return;
        var user=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync(token);
        var actor=new ActorContext(user,null,null,new HashSet<string>{"servicing"});
        if(!await db.Set<Contact>().AnyAsync(x=>x.RelationshipId==origin && x.PersonId==person && x.EndedAt==null,token) ||
            !await db.Set<Contact>().AnyAsync(x=>x.RelationshipId==target && x.PersonId==person && x.EndedAt==null,token) ||
            await db.Set<ClientAgencyRelationship>().AnyAsync(x=>(x.Id==origin || x.Id==target) && x.State!="active",token))return;
        var now=DateTimeOffset.UtcNow;var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        foreach(var shared in new[] {true,false})
        {
            var parent=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==origin,token);
            var input=SupportFlagRules.Validate(new FlagWrite(shared ? "accessible-format" : "vulnerability",shared ? "capability" : "life-event",
                shared ? "Fictional example: provide written summaries and allow additional reading time." : "Fictional internal example: arrange a consistent servicing contact for follow-up.",
                "written-consent",today.AddDays(90),"Fictional demo instruction requested by the contact.",shared ? [target] : [],shared ? "Provide written summaries and allow additional reading time." : null),today);
            var flag=await SupportFlagService.CreateAsync(db,actor,origin,person,parent.RowVersion,input,now,token);
            db.Add(new AuditEvent {ActorId=user,CreatedBy=user,OccurredAt=now,EventType="support-flag.demo-created",CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new {resourceId=flag.Id})});
            await db.SaveChangesAsync(token);
        }
    }
}
