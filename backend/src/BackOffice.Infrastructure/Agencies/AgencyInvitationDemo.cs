using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public static class AgencyInvitationDemo
{
    // Explicit fictional fixture for acceptance demos; not the agency approval workflow.
    public static async Task<Guid> Create(IDbContextFactory<BackOfficeDbContext> factory,AgencyNotificationPayload payload)
    {
        var clock=TimeProvider.System;var boundary=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,boundary,clock);
        ActorContext actor;Guid agencyId;byte[] version;
        await using(var db=await factory.CreateDbContextAsync())
        {
            var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
            var agency=new Agency{Reference="AG-DEMO-I-"+Guid.NewGuid().ToString("N")[..8],LegalName="Fictional invitation demonstration",NormalizedName="FICTIONAL INVITATION DEMONSTRATION",State="active",CreatedBy=staff.Id};
            db.Add(agency);db.Add(new AgencyOnboarding{AgencyId=agency.Id,CreatedBy=staff.Id,Details=JsonSerializer.Serialize(new{legalName=agency.LegalName})});await db.SaveChangesAsync();agencyId=agency.Id;version=agency.RowVersion;
        }
        var issuer=new InvitationService(new AgencyNotificationService(payload,clock),clock);
        var created=await new AgencyUserService(drafts,boundary,clock,issuer).Invite(actor,agencyId,Guid.NewGuid().ToString("N"),version,
            AgencyUserRules.Validate("invitation-demo-"+Guid.NewGuid().ToString("N")+"@cover.example","Fictional invited broker","broker-user"));
        using var result=JsonDocument.Parse(created.Body);return result.RootElement.GetProperty("invitationId").GetGuid();
    }
}
