using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingProofReadEndpoints
{
    private static async Task<IResult> ReferralWork(Guid draftId,Guid referralId,HttpContext context,ServicingReferralService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteEndpoints.Id(referralId);QuoteHttpInput.NoQuery(context.Request);
            var view=await service.ReadReferralsAsync(LocalIdentityService.Actor(context.User),draftId,pageSize:1,token:context.RequestAborted,referralId:referralId);
            return Results.Json(new {view.DraftId,view.CycleId,view.DraftEtag,view.Applicable,view.Items,nextCursor=(string?)null});
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    public static void MapServicingProofReads(this WebApplication app)
    {
        app.MapGet("/api/v1/drafts/{draftId:guid}/referrals/{referralId:guid}",ReferralWork).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/evidence/requirements",Requirements).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/evidence-files/{fileId:guid}/content",Download).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/evidence-files",(Guid draftId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"files",null,c,e,r,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/evidence",(Guid draftId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"associations",null,c,e,r,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/evidence/{associationId:guid}/events",(Guid draftId,Guid associationId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"events",associationId,c,e,r,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/referrals",(Guid draftId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"referrals",null,c,e,r,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/referrals/{referralId:guid}/decisions",(Guid draftId,Guid referralId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"decisions",referralId,c,e,r,p)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/referrals/{referralId:guid}/authority",(Guid draftId,Guid referralId,HttpContext c,ServicingEvidenceService e,ServicingReferralService r,PartyPaging p)=>Page(draftId,"authority",referralId,c,e,r,p)).RequireAuthorization("policy-read");
    }

    private static async Task<IResult> Requirements(Guid draftId,HttpContext context,ServicingEvidenceService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.RequirementsAsync(LocalIdentityService.Actor(context.User),draftId,context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Download(Guid draftId,Guid fileId,HttpContext context,ServicingEvidenceService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteEndpoints.Id(fileId);QuoteHttpInput.NoQuery(context.Request);
            var file=await service.DownloadAsync(LocalIdentityService.Actor(context.User),draftId,fileId,context.RequestAborted);
            context.Response.Headers.XContentTypeOptions="nosniff";
            return Results.File(file.Content,file.ContentType,file.FileName);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Page(Guid draftId,string kind,Guid? child,HttpContext context,ServicingEvidenceService evidence,ServicingReferralService referrals,PartyPaging paging)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);if(child is {} childId)QuoteEndpoints.Id(childId);
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
            var version=await evidence.HistoryVersionAsync(actor,draftId,token);
            var page=paging.ReadBound(context,actor,"servicing-proof-"+kind,version,kind=="associations"?["cycleId"]:[]);
            if(page is null || page.Size>50)throw new QuoteHttpException(400,"invalid-query");
            if(kind=="authority" && context.Request.Query.ContainsKey("pageSize") && page.Size>5)throw new QuoteHttpException(400,"invalid-query");
            object result;
            switch(kind)
            {
                case "authority":
                    var authority=await referrals.CurrentAuthorityAsync(actor,draftId,child!.Value,page.KeyId,Math.Min(page.Size,5),token);
                    if(authority.DraftEtag!=version)throw new QuoteHttpException(409,"stale-cursor");
                    result=new {authority.DraftId,authority.CycleId,authority.ReferralId,authority.DraftEtag,authority.AssessedAt,authority.Applicable,authority.CanDecide,
                        authority.Binder,authority.Items,nextCursor=paging.NextGuid(page,authority.NextAfterId)};break;
                case "files":
                    var files=await evidence.FilesAsync(actor,draftId,page.KeyId,page.Size,token);
                    result=new {files.Items,nextCursor=paging.NextGuid(page,files.NextBeforeId),draftEtag=version};break;
                case "associations":
                    var values=context.Request.Query["cycleId"];
                    if(values.Count!=1 || !Guid.TryParseExact(values[0],"D",out var cycle) || cycle==Guid.Empty)throw new QuoteHttpException(400,"cycle-scope-required");
                    var associations=await evidence.AssociationsAsync(actor,draftId,cycle,page.KeyId,page.Size,token);
                    result=new {associations.Items,nextCursor=paging.NextGuid(page,associations.NextBeforeId),draftEtag=version};break;
                case "events":
                    var events=await evidence.ReviewsAsync(actor,draftId,child!.Value,page.Offset,page.Size,token);
                    result=new {events.Items,nextCursor=paging.NextKeyset(page,events.NextAfterSequence),draftEtag=version};break;
                case "decisions":
                    var decisions=await referrals.DecisionsAsync(actor,draftId,child!.Value,page.Offset,page.Size,token);
                    result=new {decisions.ReferralId,decisions.CycleId,decisions.Items,nextCursor=paging.NextKeyset(page,decisions.NextAfterSequence),draftEtag=version};break;
                default:
                    var view=await referrals.ReadReferralsAsync(actor,draftId,page.Offset,page.Size,token);
                    if(view.DraftEtag!=version)throw new QuoteHttpException(409,"stale-cursor");
                    result=new {view.DraftId,view.CycleId,view.DraftEtag,view.Applicable,view.Items,nextCursor=paging.NextKeyset(page,view.NextAfterSequence)};break;
            }
            if(await evidence.HistoryVersionAsync(actor,draftId,token)!=version)throw new QuoteHttpException(409,"stale-cursor");
            // Draft version fences commands and cursor context; it is not a
            // cache validator for changing authority or rating applicability.
            return Results.Json(result);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
