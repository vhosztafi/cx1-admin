using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class ServicingCapacityEndpoints
{
    private static void MapReads(WebApplication app)
    {
        app.MapGet("/api/v1/drafts/{draftId:guid}/capacity",(Guid draftId,HttpContext c,ServicingCapacityReadModel r,ServicingEvidenceService e,PartyPaging p)=>Page(draftId,null,null,"cases",c,r,e,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/capacity/{caseId:guid}",Read).RequireAuthorization("policy-read");
        foreach(var kind in new[]{"submissions","messages","responses"})
        {
            var history=kind;
            app.MapGet("/api/v1/drafts/{draftId:guid}/capacity/{caseId:guid}/"+history,
                (Guid draftId,Guid caseId,HttpContext c,ServicingCapacityReadModel r,ServicingEvidenceService e,PartyPaging p)=>Page(draftId,caseId,null,history,c,r,e,p)).RequireAuthorization("policy-read");
        }
        app.MapGet("/api/v1/drafts/{draftId:guid}/capacity/{caseId:guid}/conditions/{conditionId:guid}/resolutions",
            (Guid draftId,Guid caseId,Guid conditionId,HttpContext c,ServicingCapacityReadModel r,ServicingEvidenceService e,PartyPaging p)=>Page(draftId,caseId,conditionId,"resolutions",c,r,e,p)).RequireAuthorization("policy-read");
    }
    private static async Task<IResult> Read(Guid draftId,Guid caseId,HttpContext context,ServicingCapacityReadModel reads)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteEndpoints.Id(caseId);QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await reads.GetAsync(LocalIdentityService.Actor(context.User),draftId,caseId,context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    private static async Task<IResult> Page(Guid draftId,Guid? caseId,Guid? conditionId,string kind,HttpContext context,
        ServicingCapacityReadModel reads,ServicingEvidenceService evidence,PartyPaging paging)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);if(caseId is Guid id)QuoteEndpoints.Id(id);if(conditionId is Guid condition)QuoteEndpoints.Id(condition);
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
            var version=await evidence.HistoryVersionAsync(actor,draftId,token);
            var page=paging.ReadBound(context,actor,"servicing-capacity-"+kind,version,kind=="cases"?["referralId"]:[]);
            if(page is null || page.Size>50) throw new QuoteHttpException(400,"invalid-query");
            Guid? referralId=null;
            if(context.Request.Query.TryGetValue("referralId",out var filter))
            {if(!Guid.TryParseExact(filter.ToString(),"D",out var parsed)||parsed==Guid.Empty)throw new QuoteHttpException(400,"invalid-query");referralId=parsed;}
            object result;
            switch(kind)
            {
                case "cases":
                    var cases=await reads.ListAsync(actor,draftId,page.KeyId,page.Size,token,referralId);
                    if(cases.DraftEtag!=version)throw new QuoteHttpException(409,"stale-cursor");
                    result=new{cases.DraftId,cases.PolicyId,cases.Items,nextCursor=paging.NextGuid(page,cases.NextBeforeId),draftEtag=version};break;
                case "submissions":
                    var submissions=await reads.SubmissionsAsync(actor,draftId,caseId!.Value,page.Offset,page.Size,token);
                    result=new{submissions.Items,nextCursor=paging.NextKeyset(page,submissions.NextAfterSequence),draftEtag=version};break;
                case "messages":
                    var messages=await reads.MessagesAsync(actor,draftId,caseId!.Value,page.Offset,page.Size,token);
                    result=new{messages.Items,nextCursor=paging.NextKeyset(page,messages.NextAfterSequence),draftEtag=version};break;
                case "responses":
                    var responses=await reads.ResponsesAsync(actor,draftId,caseId!.Value,page.Offset,page.Size,token);
                    result=new{responses.Items,nextCursor=paging.NextKeyset(page,responses.NextAfterSequence),draftEtag=version};break;
                default:
                    var resolutions=await reads.ResolutionsAsync(actor,draftId,caseId!.Value,conditionId!.Value,page.Offset,page.Size,token);
                    result=new{resolutions.Items,nextCursor=paging.NextKeyset(page,resolutions.NextAfterSequence),draftEtag=version};break;
            }
            if(await evidence.HistoryVersionAsync(actor,draftId,token)!=version)throw new QuoteHttpException(409,"stale-cursor");
            return Results.Json(result);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
