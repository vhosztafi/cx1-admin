using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class ServicingCapacityEndpoints
{
    public static void MapServicingCapacity(this WebApplication app)
    {
        app.MapPost("/api/v1/drafts/{draftId:guid}/capacity",Create).RequireAuthorization("underwriting-escalate");
        foreach(var kind in new[]{"submissions","query-replies","chases","actions","assignment","responses"})
        {
            var operation=kind;
            app.MapPost("/api/v1/drafts/{draftId:guid}/capacity/{caseId:guid}/"+operation,
                (Guid draftId,Guid caseId,HttpContext c,ServicingCapacityService s)=>Mutate(draftId,caseId,operation,c,s))
                .RequireAuthorization(operation=="responses"?"underwriting-record-capacity":"underwriting-escalate");
        }
        app.MapPost("/api/v1/drafts/{draftId:guid}/capacity-conditions/{conditionId:guid}/resolutions",Resolve).RequireAuthorization("underwriting-decide-within-authority");
        MapReads(app);
    }

    private static (byte[] Version,Guid Lease,string Key) Command(Guid draftId,HttpContext context)
    {
        QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
        return(QuoteHttpInput.Version(context.Request),ServicingEndpoints.Fence(context.Request),QuoteHttpInput.Key(context.Request));
    }
    private static IResult Outcome(HttpContext context,CommandOutcome outcome)
    {
        context.Response.Headers.ETag=outcome.Etag;return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);
    }

    private static async Task<IResult> Create(Guid draftId,HttpContext context,ServicingCapacityService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","referralId","referralEtag","reason");
            var outcome=await service.CreateAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),QuoteHttpInput.Id(root,"referralId"),
                command.Version,QuoteReferralEndpoints.Version(root,"referralEtag"),command.Lease,QuoteReferralEndpoints.Text(root,"reason",2000),command.Key,Guid.NewGuid(),token);
            context.Response.Headers.Location=$"/api/v1/drafts/{draftId:D}/capacity/{outcome.ResourceId:D}";return Outcome(context,outcome);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Mutate(Guid draftId,Guid caseId,string kind,HttpContext context,ServicingCapacityService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);QuoteEndpoints.Id(caseId);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,262144);var root=document.RootElement;
            var fields=kind switch{
                "submissions"=>new[]{"cycleId","caseEtag","body","reason","evidenceAssociationIds","scenarioVersionId"},
                "query-replies"=>["cycleId","caseEtag","responseId","body","reason","evidenceAssociationIds","scenarioVersionId"],
                "chases"=>["cycleId","caseEtag","submissionId","body","reason"],
                "actions"=>["cycleId","caseEtag","action","reason"],
                "assignment"=>["cycleId","caseEtag","assignedUserId","reason"],
                "responses"=>["cycleId","caseEtag","submissionId","evidenceAssociationId","definition","body","providerUnderwriter","providerReference","receivedAt","reason"],
                _=>throw new QuoteHttpException(422,"servicing-capacity-command-invalid")};
            QuoteHttpInput.Keys(root,fields);var actor=LocalIdentityService.Actor(context.User);var cycle=QuoteHttpInput.Id(root,"cycleId");
            var version=QuoteReferralEndpoints.Version(root,"caseEtag");var reason=QuoteReferralEndpoints.Text(root,"reason",2000);var correlation=Guid.NewGuid();
            var result=kind switch{
                "submissions"=>await service.SubmitAsync(actor,draftId,cycle,caseId,command.Version,version,command.Lease,QuoteReferralEndpoints.Text(root,"body",10000),reason,
                    EvidenceIds(root),QuoteHttpInput.Id(root,"scenarioVersionId"),command.Key,correlation,token),
                "query-replies"=>await service.ReplyAsync(actor,draftId,cycle,caseId,QuoteHttpInput.Id(root,"responseId"),command.Version,version,command.Lease,
                    QuoteReferralEndpoints.Text(root,"body",10000),reason,EvidenceIds(root),QuoteHttpInput.Id(root,"scenarioVersionId"),command.Key,correlation,token),
                "chases"=>await service.ChaseAsync(actor,draftId,cycle,caseId,QuoteHttpInput.Id(root,"submissionId"),command.Version,version,command.Lease,
                    QuoteReferralEndpoints.Text(root,"body",10000),reason,command.Key,correlation,token),
                "actions"=>await service.ActionAsync(actor,draftId,cycle,caseId,command.Version,version,command.Lease,QuoteReferralEndpoints.Text(root,"action",20),reason,command.Key,correlation,token),
                "assignment"=>await service.AssignAsync(actor,draftId,cycle,caseId,command.Version,version,command.Lease,QuoteHttpInput.Id(root,"assignedUserId"),reason,command.Key,correlation,token),
                "responses"=>await service.RecordResponseAsync(actor,draftId,cycle,caseId,QuoteHttpInput.Id(root,"submissionId"),command.Version,version,command.Lease,
                    QuoteHttpInput.Id(root,"evidenceAssociationId"),Definition(root),QuoteReferralEndpoints.Text(root,"body",10000),QuoteReferralEndpoints.Text(root,"providerUnderwriter",200),
                    QuoteReferralEndpoints.Text(root,"providerReference",100),Instant(root,"receivedAt"),reason,command.Key,correlation,token),
                _=>throw new QuoteHttpException(422,"servicing-capacity-command-invalid")};
            return Outcome(context,result);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Resolve(Guid draftId,Guid conditionId,HttpContext context,ServicingCapacityService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);QuoteEndpoints.Id(conditionId);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","conditionEtag","evidenceAssociationId","outcome","reason");
            return Outcome(context,await service.ResolveConditionAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),conditionId,
                command.Version,QuoteReferralEndpoints.Version(root,"conditionEtag"),command.Lease,QuoteHttpInput.Id(root,"evidenceAssociationId"),
                QuoteReferralEndpoints.Text(root,"outcome",20),QuoteReferralEndpoints.Text(root,"reason",2000),command.Key,Guid.NewGuid(),token));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static Guid[] EvidenceIds(JsonElement root)=>Array(root,"evidenceAssociationIds",20).Select(value=>
        value.ValueKind==JsonValueKind.String && Guid.TryParseExact(value.GetString(),"D",out var id) && id!=Guid.Empty?id:
            throw new QuoteHttpException(422,"servicing-capacity-evidence-invalid")).ToArray();

    private static ServicingCapacityResponseDefinition Definition(JsonElement root)
    {
        if(!root.TryGetProperty("definition",out var value) || value.ValueKind!=JsonValueKind.Object) throw new QuoteHttpException(422,"servicing-capacity-response-required");
        QuoteHttpInput.Keys(value,"outcome","validFrom","validTo","authorisedLimits","conditions");
        DateTimeOffset? Optional(string name)=>value.TryGetProperty(name,out var date) && date.ValueKind==JsonValueKind.Null?null:Instant(value,name);
        var conditions=Array(value,"conditions",20).Select(x=>{
            if(x.ValueKind!=JsonValueKind.Object)throw new QuoteHttpException(422,"servicing-condition-invalid");
            QuoteHttpInput.Keys(x,"definition","effectiveDates");
            if(!x.TryGetProperty("definition",out var clause) || clause.ValueKind!=JsonValueKind.Object)throw new QuoteHttpException(422,"servicing-condition-invalid");
            var dates=Array(x,"effectiveDates",100).Select(d=>{
                if(d.ValueKind!=JsonValueKind.String)throw new QuoteHttpException(422,"servicing-capacity-time-invalid");return ParseInstant(d.GetString()!);}).ToArray();
            return new ServicingCarrierConditionInput(clause.Clone(),dates);
        }).ToArray();
        return new(QuoteReferralEndpoints.Text(value,"outcome",30),Optional("validFrom"),Optional("validTo"),Array(value,"authorisedLimits",20),conditions);
    }
    private static JsonElement[] Array(JsonElement root,string field,int maximum)
    {
        if(!root.TryGetProperty(field,out var value) || value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()>maximum)
            throw new QuoteHttpException(422,"servicing-capacity-array-invalid");
        return value.EnumerateArray().Select(x=>x.Clone()).ToArray();
    }
    private static DateTimeOffset Instant(JsonElement root,string field)=>ParseInstant(QuoteReferralEndpoints.Text(root,field,40));
    private static DateTimeOffset ParseInstant(string value)
    {
        var offset=value.EndsWith('Z') || value.Length>=6 && value[^6] is '+' or '-';
        if(!offset || !DateTimeOffset.TryParseExact(value,["yyyy-MM-dd'T'HH:mm:ssK","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed) || parsed.Offset!=TimeSpan.Zero)
            throw new QuoteHttpException(422,"servicing-capacity-time-invalid");return parsed;
    }
}
