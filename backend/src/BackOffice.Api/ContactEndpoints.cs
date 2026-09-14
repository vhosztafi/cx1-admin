using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class ContactEndpoints
{
    public static void MapContacts(this WebApplication app)
    {
        const string path="/api/v1/relationships/{relationshipId:guid}/contacts";
        app.MapGet(path,List).RequireAuthorization("relationship-read");
        app.MapGet(path+"/{contactId:guid}",Get).RequireAuthorization("relationship-read");
        app.MapPost(path,Create).RequireAuthorization("contact-write");
        app.MapPut(path+"/{contactId:guid}",Update).RequireAuthorization("contact-write");
        app.MapPost(path+"/{contactId:guid}/make-primary",MakePrimary).RequireAuthorization("contact-write");
        app.MapPost(path+"/{contactId:guid}/end",End).RequireAuthorization("contact-write");
    }
    private static async Task<IResult> List(Guid relationshipId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        if(!await scope.Relationships(db).AnyAsync(x=>x.Id==relationshipId,context.RequestAborted))return Missing(context);
        var page=paging.Read(context,actor,"id","includeEnded");var include=context.Request.Query["includeEnded"].ToString();
        if(page is null || include is not ("" or "true" or "false"))return IdentityEndpoints.Problem(context,400,"invalid-query","Use supported contact filters.");
        var query=scope.Contacts(db,includeEnded:include=="true").Where(x=>x.RelationshipId==relationshipId && x.CreatedAt<=page.AsOf);
        var total=await query.CountAsync(context.RequestAborted);
        var rows=await query.OrderBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        return Results.Json(new {items=rows.Select(View),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<IResult> Get(Guid relationshipId,Guid contactId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var row=await new PartyScope(LocalIdentityService.Actor(context.User)).Contacts(db,includeEnded:true)
            .SingleOrDefaultAsync(x=>x.Id==contactId && x.RelationshipId==relationshipId,context.RequestAborted);
        if(row is null)return Missing(context);
        context.Response.Headers.ETag=Etag(row.RowVersion);return Results.Json(View(row),ClientEndpoints.Json);
    }
    private static Task<IResult> Create(Guid relationshipId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(relationshipId,null,"create",input,context,factory,commands,time);
    private static Task<IResult> Update(Guid relationshipId,Guid contactId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(relationshipId,contactId,"update",input,context,factory,commands,time);
    private static Task<IResult> MakePrimary(Guid relationshipId,Guid contactId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(relationshipId,contactId,"make-primary",null,context,factory,commands,time);
    private static Task<IResult> End(Guid relationshipId,Guid contactId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(relationshipId,contactId,"end",input,context,factory,commands,time);

    private static async Task<IResult> Write(Guid relationshipId,Guid? contactId,string operation,JsonElement? input,HttpContext context,
        IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
            await using var read=await factory.CreateDbContextAsync(token);
            await ContactService.AuthorizeAsync(read,actor,relationshipId,contactId,token:token);
            ValidatedContact? contactInput=null;string? reason=null;
            if(operation is "create" or "update")contactInput=ContactRules.Validate(ClientEndpoints.Input<ContactWrite>(input!.Value),time.GetUtcNow());
            if(operation=="end")
            {
                reason=ClientEndpoints.Input<EndInput>(input!.Value).Reason;
                if(string.IsNullOrWhiteSpace(reason) || reason.Length>1000 || reason.Any(char.IsControl))throw new ContactOperationException(422,"end-reason-required");
                reason=reason.Trim();
            }
            if(operation=="create" && contactInput!.PersonId is Guid person)
                await ContactService.AuthorizeAsync(read,actor,relationshipId,reusablePersonId:person,token:token);
            var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);
            var route=$"/api/v1/relationships/{relationshipId}/contacts"+(contactId is null ? "" : $"/{contactId}")+(operation is "end" or "make-primary" ? "/"+operation : "");
            var eventType=operation switch {"create"=>"contact.created","update"=>"contact.updated","end"=>"contact.ended",_=>"contact.primary-changed"};
            // Preconditions are deliberately outside the immutable intent hash, and inside the handler after replay.
            var result=await commands.ExecuteAsync(new CommandIdentity(actor.UserId,route,key,Guid.NewGuid()),
                new {operation,contactInput,reason},eventType,async(db,ct)=>
                {
                    var now=time.GetUtcNow();
                    var row=operation switch
                    {
                        "create"=>await ContactService.CreateAsync(db,actor,relationshipId,version,contactInput!,now,ct),
                        "update"=>await ContactService.UpdateAsync(db,actor,relationshipId,contactId!.Value,version,contactInput!,now,ct),
                        "end"=>await ContactService.EndAsync(db,actor,relationshipId,contactId!.Value,version,reason!,now,ct),
                        _=>await ContactService.MakePrimaryAsync(db,actor,relationshipId,contactId!.Value,version,now,ct)
                    };
                    return new CommandOutcome(row.Id,operation=="create" ? 201 : 200,JsonSerializer.Serialize(View(row),ClientEndpoints.Json),Etag:Etag(row.RowVersion));
                },token);
            context.Response.Headers.ETag=result.Etag;
            if(result.Status==201)context.Response.Headers.Location=$"/api/v1/relationships/{relationshipId}/contacts/{result.ResourceId}";
            return Results.Content(result.Body,"application/json",statusCode:result.Status);
        }
        catch(ContactOperationException error)
        {
            var title=error.Code=="choose-replacement-primary" ? "Make another active contact primary before changing or ending this primary contact."
                : error.Status==404 ? "Record not found." : "Refresh the contact and check the request before retrying.";
            return IdentityEndpoints.Problem(context,error.Status,error.Code,title);
        }
        catch(Exception error) when(ClientEndpoints.IsCommandError(error)){return ClientEndpoints.CommandError(context,error,"contact");}
    }
    private static object View(Contact row)=>new {row.Id,row.PersonId,row.RelationshipId,fullName=row.DeclaredFullName,firstName=row.DeclaredFirstName,
        surname=row.DeclaredSurname,row.Role,row.Email,row.Telephone,row.IsPrimary,
        marketingConsent=JsonSerializer.Deserialize<MarketingConsentWrite>(row.MarketingConsent,ClientEndpoints.Json),row.EndedAt};
    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"contact-record-not-found","Record not found.");
    private sealed record EndInput([property:JsonRequired]string Reason);
}
