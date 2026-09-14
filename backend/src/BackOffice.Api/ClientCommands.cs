using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static partial class ClientEndpoints
{
    private static Task<IResult> Create(JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        => Write(null,input,context,factory,commands,time);
    private static Task<IResult> Update(Guid clientId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        => Write(clientId,input,context,factory,commands,time);

    private static async Task<IResult> Write(Guid? clientId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);
            if(clientId is Guid existingId)
            {
                await using var read=await factory.CreateDbContextAsync(context.RequestAborted);
                if(!await new PartyScope(actor).Clients(read).AnyAsync(x => x.Id==existingId,context.RequestAborted))return Missing(context);
            }
            var identity=ClientIdentity.Validate(Input<ClientWrite>(input));
            var key=Key(context);var version=clientId is null ? null : Version(context);
            var route=clientId is null ? "/api/v1/clients" : $"/api/v1/clients/{clientId}";
            var eventType=clientId is null ? "client.created" : "client.updated";
            var result=await commands.ExecuteAsync(new CommandIdentity(actor.UserId,route,key,Guid.NewGuid()),identity,eventType,async (db,token) =>
            {
                var now=time.GetUtcNow();
                ClientAccount row;
                if(clientId is Guid id) row=await LockClient(db,id,version!,token);
                else {row=new ClientAccount {Reference=await ClientReferences.NextAsync(db,token),CreatedBy=actor.UserId,CreatedAt=now};db.Add(row);}
                row.LegalName=identity.LegalName;row.NormalizedName=identity.NormalizedName;row.EntityType=identity.EntityType;
                row.CompanyNumber=identity.CompanyNumber;row.Address=JsonSerializer.Serialize(identity.Address,Json);row.UpdatedAt=now;
                if(clientId is not null)db.Entry(row).Property(x => x.UpdatedAt).IsModified=true;
                AddActivity(db,row.Id,null,actor.UserId,eventType,now);
                await db.SaveChangesAsync(token);
                return new CommandOutcome(row.Id,clientId is null ? 201 : 200,JsonSerializer.Serialize(View(row),Json),Etag:Etag(row.RowVersion));
            },context.RequestAborted);
            return CommandResponse(context,result,"/api/v1/clients/");
        }
        catch(Exception exception) when(IsCommandError(exception)) {return CommandError(context,exception);}
    }

    private static async Task<IResult> CreateRelationship(Guid clientId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);
            await using var read=await factory.CreateDbContextAsync(context.RequestAborted);
            if(!await scope.Clients(read).AnyAsync(x => x.Id==clientId,context.RequestAborted))return Missing(context);
            var request=Input<RelationshipInput>(input);
            if(request.AgencyId==Guid.Empty)throw new PartyCommandException(422,"invalid-agency");
            // Both parents are authorized before even looking for a cached command receipt.
            if(!await scope.Agencies(read).AnyAsync(x => x.Id==request.AgencyId,context.RequestAborted))return Missing(context);
            var key=Key(context);var version=Version(context);
            var result=await commands.ExecuteAsync(new CommandIdentity(actor.UserId,$"/api/v1/clients/{clientId}/relationships",key,Guid.NewGuid()),
                request,"client.relationship-created",async (db,token) =>
                {
                    // Serialize association changes with agency lifecycle decisions before locking children.
                    var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={request.AgencyId}").SingleOrDefaultAsync(token) ?? throw new PartyCommandException(404,"client-record-not-found");
                    if(agency.State is "suspended" or "abandoned")throw new PartyCommandException(409,"agency-unavailable");
                    var client=await LockClient(db,clientId,version,token);
                    if(await db.Set<ClientAgencyRelationship>().AnyAsync(x => x.ClientId==clientId && x.AgencyId==request.AgencyId,token))
                        throw new PartyCommandException(409,"relationship-exists");
                    var now=time.GetUtcNow();
                    var row=new ClientAgencyRelationship {ClientId=clientId,AgencyId=request.AgencyId,CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                    db.Add(row);client.UpdatedAt=now;
                    // Always update the parent token, even when a test/frozen clock repeats a timestamp.
                    db.Entry(client).Property(x => x.UpdatedAt).IsModified=true;
                    AddActivity(db,clientId,row.Id,actor.UserId,"client.relationship-created",now);
                    await db.SaveChangesAsync(token);
                    return new CommandOutcome(row.Id,201,JsonSerializer.Serialize(RelationshipView(row,agency),Json),Etag:Etag(row.RowVersion));
                },context.RequestAborted);
            return CommandResponse(context,result,"/api/v1/relationships/");
        }
        catch(Exception exception) when(IsCommandError(exception)) {return CommandError(context,exception);}
    }

    private static async Task<ClientAccount> LockClient(BackOfficeDbContext db,Guid id,byte[] expected,CancellationToken token)
    {
        var row=await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM [ClientAccount] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(token)
            ?? throw new PartyCommandException(404,"client-record-not-found");
        if(!CryptographicOperations.FixedTimeEquals(row.RowVersion,expected))throw new PartyCommandException(412,"stale-client");
        return row;
    }
    private static void AddActivity(BackOfficeDbContext db,Guid clientId,Guid? relationshipId,Guid actorId,string eventType,DateTimeOffset now)
        => db.Add(new ClientActivity {ClientId=clientId,RelationshipId=relationshipId,ActorId=actorId,CreatedBy=actorId,CreatedAt=now,OccurredAt=now,
            EventType=eventType,RecordId=clientId,RecordKind="client"});
    private static IResult CommandResponse(HttpContext context,CommandOutcome result,string location)
    {
        context.Response.Headers.ETag=result.Etag;
        if(result.Status==201)context.Response.Headers.Location=location+result.ResourceId;
        return Results.Content(result.Body,"application/json",statusCode:result.Status);
    }
    internal static T Input<T>(JsonElement input)
    {
        // Contracts allow omission of optional fields, but never an explicit null or duplicate key.
        ValidateShape(input);
        return input.Deserialize<T>(Json) ?? throw new PartyCommandException(400,"invalid-request");
    }
    private static void ValidateShape(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Null)throw new PartyCommandException(422,"null-field");
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var property in element.EnumerateObject())
            {
                if(!names.Add(property.Name))throw new PartyCommandException(400,"duplicate-field");
                ValidateShape(property.Value);
            }
        }
        if(element.ValueKind==JsonValueKind.Array)foreach(var value in element.EnumerateArray())ValidateShape(value);
    }
    internal static string Key(HttpContext context)
    {
        var values=context.Request.Headers["Idempotency-Key"];
        if(values.Count!=1 || values[0] is not {Length:>=16 and <=200} key || key!=key.Trim() || key.Any(char.IsControl))
            throw new PartyCommandException(400,"idempotency-key-required");
        return key;
    }
    internal static byte[] Version(HttpContext context)
    {
        var values=context.Request.Headers.IfMatch;
        if(values.Count==0)throw new PartyCommandException(428,"version-required");
        if(values.Count==1 && values[0] is {Length:14} text && text[0]=='"' && text[^1]=='"')
        {
            try {var bytes=Convert.FromBase64String(text[1..^1]);if(bytes.Length==8 && Etag(bytes)==text)return bytes;}
            catch(FormatException) { }
        }
        throw new PartyCommandException(400,"invalid-version");
    }
    internal static bool IsCommandError(Exception error)=>error is PartyCommandException or PartyValidationException or JsonException or CommandKeyConflictException or CommandBusyException or DbUpdateConcurrencyException ||
        error is DbUpdateException {InnerException:SqlException {Number:2601 or 2627}};
    internal static IResult CommandError(HttpContext context,Exception error,string resource="client")
    {
        if(error is PartyValidationException validation)return Results.Problem(statusCode:422,title:$"Check the {resource} fields.",extensions:
            new Dictionary<string,object?> {{"code","invalid-"+resource},{"traceId",context.TraceIdentifier},{"errors",validation.Issues}});
        var (status,code)=error switch
        {
            PartyCommandException command=>(command.Status,command.Code),JsonException=>(400,"invalid-request"),
            CommandKeyConflictException=>(409,"idempotency-conflict"),CommandBusyException=>(409,"command-busy"),
            DbUpdateConcurrencyException=>(412,"stale-"+resource),_=>(409,resource=="client" ? "relationship-exists" : resource+"-conflict")
        };
        return IdentityEndpoints.Problem(context,status,code,"Refresh the record and check the request before retrying.");
    }
    private sealed class PartyCommandException(int status,string code):Exception {public int Status{get;}=status;public string Code{get;}=code;}
    private sealed record RelationshipInput([property:JsonRequired] Guid AgencyId);
}
