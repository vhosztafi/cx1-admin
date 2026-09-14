using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;

namespace BackOffice.Api;

public static class InvitationEndpoints
{
    public static void MapInvitations(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/invitations/accept",Accept).AllowAnonymous().RequireRateLimiting("invitation-acceptance");
        if(app.Environment.IsDevelopment())app.MapPost("/api/v1/invitations/{invitationId:guid}/demo-link",Reveal).RequireAuthorization("agency-admin");
    }
    private static async Task<IResult> Accept(HttpContext context,InvitationAcceptance acceptance)
    {
        try
        {
            if(!context.Request.HasJsonContentType())throw new AgencyCommandException(415,"json-required");
            if(context.Request.ContentLength>2048)throw new AgencyCommandException(413,"invitation-body-size");
            using var stream=new MemoryStream();var bytes=new byte[2048];int read;
            while((read=await context.Request.Body.ReadAsync(bytes,context.RequestAborted))>0)
            {if(stream.Length+read>2048)throw new AgencyCommandException(413,"invitation-body-size");stream.Write(bytes,0,read);}
            stream.Position=0;using var document=await JsonDocument.ParseAsync(stream,new JsonDocumentOptions{MaxDepth=4},context.RequestAborted);
            var input=ClientEndpoints.Input<AcceptInput>(document.RootElement);
            return await acceptance.Accept(input.InvitationToken,input.Password,context.RequestAborted)?Results.Json(new{accepted=true}):IdentityEndpoints.Problem(context,400,"invalid-invitation","This invitation link is invalid or no longer available.");
        }
        catch(AgencyCommandException ex){return IdentityEndpoints.Problem(context,ex.Status,ex.Code,ex.Code=="invalid-invitation-password"?"Use a password between 12 and 128 characters.":"Check the invitation request.");}
        catch(Exception ex)when(ClientEndpoints.IsCommandError(ex)||ex is ArgumentException)
        {return ex is ArgumentException?IdentityEndpoints.Problem(context,400,"invalid-invitation-request","Check the invitation request."):ClientEndpoints.CommandError(context,ex,"invitation");}
    }
    private static async Task<IResult> Reveal(Guid invitationId,HttpContext context,InvitationDemoReveal reveal)
    {
        try{return Results.Json(new{invitationToken=await reveal.Reveal(LocalIdentityService.Actor(context.User),invitationId,context.RequestAborted)});}
        catch(AgencyCommandException ex){return IdentityEndpoints.Problem(context,ex.Status,ex.Code,"Invitation link is unavailable.");}
    }
    private sealed class AcceptInput
    {
        [JsonRequired]public string? InvitationToken{get;init;}
        [JsonRequired]public string? Password{get;init;}
        public override string ToString()=>"Protected invitation acceptance";
    }
}
