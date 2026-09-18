using System.Globalization;
using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingTermsEndpoints
{
    public static void MapServicingTerms(this WebApplication app)
    {
        app.MapGet("/api/v1/drafts/{draftId:guid}/terms",Read).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/terms/history/{kind}",History).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/terms/history/terms/{termsId:guid}",Retained).RequireAuthorization("policy-read");
        foreach(var kind in new[]{"terms/prepare","terms/send","acceptances"})
        {
            var operation=kind;
            app.MapPost("/api/v1/drafts/{draftId:guid}/"+operation,
                (Guid draftId,HttpContext c,ServicingTermsService s)=>Command(draftId,operation,c,s)).RequireAuthorization("policy-draft-write");
        }
    }

    private static async Task<IResult> History(Guid draftId,string kind,HttpContext context,ServicingTermsService service,ServicingEvidenceService evidence,PartyPaging paging)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);if(kind is not("terms" or "deliveries" or "acceptances"))throw new QuoteHttpException(404,"servicing-history-not-found");
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;var version=await evidence.HistoryVersionAsync(actor,draftId,token);
            var page=paging.ReadBound(context,actor,"servicing-terms-"+kind,version,[]);
            if(page is null || page.Size>50)throw new QuoteHttpException(400,"invalid-query");
            var history=await service.HistoryAsync(actor,draftId,kind,page.KeyId,page.Size,token);
            if(await evidence.HistoryVersionAsync(actor,draftId,token)!=version)throw new QuoteHttpException(409,"stale-cursor");
            return Results.Json(new{history.Items,nextCursor=paging.NextGuid(page,history.NextBeforeId),draftEtag=version});
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Retained(Guid draftId,Guid termsId,HttpContext context,ServicingTermsService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteEndpoints.Id(termsId);QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.RetainedAsync(LocalIdentityService.Actor(context.User),draftId,termsId,context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Read(Guid draftId,HttpContext context,ServicingTermsService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.ReadAsync(LocalIdentityService.Actor(context.User),draftId,context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Command(Guid draftId,string kind,HttpContext context,ServicingTermsService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
            var version=QuoteHttpInput.Version(context.Request);var lease=ServicingEndpoints.Fence(context.Request);var key=QuoteHttpInput.Key(context.Request);
            var token=context.RequestAborted;using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,kind switch{
                "terms/prepare"=>["cycleId","ratingId","templateVersionId"],
                "terms/send"=>["cycleId","termsVersionId","recipientContactIds"],
                _=>["cycleId","ratingId","termsVersionId","deliveryId","termsHash","assuranceHash","accepterLabel","acceptedAt","channel","evidenceAssociationId"]});
            var actor=LocalIdentityService.Actor(context.User);var cycle=QuoteHttpInput.Id(root,"cycleId");var correlation=Guid.NewGuid();
            var outcome=kind switch{
                "terms/prepare"=>await service.PrepareAsync(actor,draftId,cycle,QuoteHttpInput.Id(root,"ratingId"),QuoteHttpInput.Id(root,"templateVersionId"),version,lease,key,correlation,token),
                "terms/send"=>await service.SendAsync(actor,draftId,cycle,QuoteHttpInput.Id(root,"termsVersionId"),Recipients(root),version,lease,key,correlation,token),
                _=>await service.AcceptAsync(actor,draftId,version,lease,new(cycle,QuoteHttpInput.Id(root,"ratingId"),QuoteHttpInput.Id(root,"termsVersionId"),
                    QuoteHttpInput.Id(root,"deliveryId"),QuoteReferralEndpoints.Text(root,"termsHash",64),QuoteReferralEndpoints.Text(root,"assuranceHash",64),
                    QuoteReferralEndpoints.Text(root,"accepterLabel",200),Instant(root),QuoteReferralEndpoints.Text(root,"channel",30),QuoteHttpInput.Id(root,"evidenceAssociationId")),key,correlation,token)};
            context.Response.Headers.ETag=outcome.Etag;return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static Guid[] Recipients(JsonElement root)
    {
        if(!root.TryGetProperty("recipientContactIds",out var items) || items.ValueKind!=JsonValueKind.Array || items.GetArrayLength() is <1 or >20)
            throw new QuoteHttpException(422,"servicing-recipient-invalid");
        var ids=items.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String && Guid.TryParseExact(x.GetString(),"D",out var id) && id!=Guid.Empty?id:
            throw new QuoteHttpException(422,"servicing-recipient-invalid")).ToArray();
        if(ids.Distinct().Count()!=ids.Length)throw new QuoteHttpException(422,"servicing-recipient-invalid");return ids;
    }

    private static DateTimeOffset Instant(JsonElement root)
    {
        var value=QuoteReferralEndpoints.Text(root,"acceptedAt",40);
        var offset=value.EndsWith('Z') || value.Length>=6 && value[^6] is '+' or '-';
        if(!offset || !DateTimeOffset.TryParseExact(value,["yyyy-MM-dd'T'HH:mm:ssK","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed) || parsed.Offset!=TimeSpan.Zero)
            throw new QuoteHttpException(422,"servicing-acceptance-time-invalid");return parsed;
    }
}
