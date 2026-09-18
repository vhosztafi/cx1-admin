using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class RenewalPreparationEndpoints
{
    public static void MapRenewalPreparation(this WebApplication app)
    {
        app.MapGet("/api/v1/terms/{termId:guid}/renewal-preview",Preview).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}/renewal/experience",ReadExperience).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/drafts/{draftId:guid}/renewal/preparation",Prepare).RequireAuthorization("quote-rate");
        app.MapPost("/api/v1/drafts/{draftId:guid}/renewal/experience/uploads",Upload).RequireAuthorization("underwriting-evidence-write");
        app.MapPut("/api/v1/drafts/{draftId:guid}/renewal/experience",SaveExperience).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/renewal/experience/{experienceId:guid}/reviews",ReviewExperience).RequireAuthorization("underwriting-evidence-review");
    }

    private static async Task<IResult> Preview(Guid termId,HttpContext context,RenewalPreparationService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(termId);
            if(context.Request.Query.Keys.Any(x=>x is not("termMonths" or "endUtcOffsetMinutes")))throw new QuoteHttpException(400,"invalid-query");
            int? Number(string name)
            {
                if(!context.Request.Query.TryGetValue(name,out var value))return null;
                if(value.Count!=1 || !int.TryParse(value[0],NumberStyles.None,CultureInfo.InvariantCulture,out var parsed))throw new QuoteHttpException(400,"invalid-query");
                return parsed;
            }
            var view=await service.PreviewAsync(LocalIdentityService.Actor(context.User),termId,Number("termMonths"),Number("endUtcOffsetMinutes"),context.RequestAborted);
            context.Response.Headers.ETag=view.TermEtag;return Results.Json(view);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> ReadExperience(Guid draftId,HttpContext context,RenewalPreparationService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
            var view=await service.ReadExperienceAsync(LocalIdentityService.Actor(context.User),draftId,context.RequestAborted);
            context.Response.Headers.ETag=view.Etag;var row=view.Experience;var review=view.Review;
            return Results.Json(new{view.DraftId,view.EvidenceFileId,
                experience=row is null?null:new{row.Id,row.Sequence,row.ObservationStartsOn,row.ObservationEndsOn,row.ClaimCount,
                    paid=Money(row.Paid),outstanding=Money(row.Outstanding),earnedPremium=Money(row.EarnedPremium),row.SourceCode,row.SourceReference,
                    row.EvidenceAssociationId,recordedAt=row.CreatedAt,recordedBy=row.CreatedBy},
                review=review is null?null:new{review.Id,review.ExperienceVersionId,review.Outcome,review.Reason,review.AuthorityVersionId,review.AuthorityGrantId,
                    recordedAt=review.CreatedAt,recordedBy=review.CreatedBy}});
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Prepare(Guid draftId,HttpContext context,RenewalPreparationService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);using var document=await QuoteHttpInput.Read(context.Request,context.RequestAborted,8192);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"termMonths","endUtcOffsetMinutes");
            return Outcome(context,await service.PrepareAsync(LocalIdentityService.Actor(context.User),draftId,command.Version,command.Lease,
                Number(root,"termMonths"),root.TryGetProperty("endUtcOffsetMinutes",out _)?Number(root,"endUtcOffsetMinutes"):null,command.Key,Guid.NewGuid(),context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> SaveExperience(Guid draftId,HttpContext context,RenewalPreparationService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);using var document=await QuoteHttpInput.Read(context.Request,context.RequestAborted,8192);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"observationStartsOn","observationEndsOn","claimCount","paid","outstanding","earnedPremium","sourceCode","sourceReference","evidenceAssociationId");
            var input=new RenewalExperienceFacts(Day(root,"observationStartsOn"),Day(root,"observationEndsOn"),Number(root,"claimCount"),
                Amount(root,"paid"),Amount(root,"outstanding"),Amount(root,"earnedPremium"),QuoteReferralEndpoints.Text(root,"sourceCode",30),
                QuoteReferralEndpoints.Text(root,"sourceReference",200),QuoteHttpInput.Id(root,"evidenceAssociationId"));
            return Outcome(context,await service.SaveExperienceAsync(LocalIdentityService.Actor(context.User),draftId,command.Version,command.Lease,input,command.Key,Guid.NewGuid(),context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> ReviewExperience(Guid draftId,Guid experienceId,HttpContext context,RenewalPreparationService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);QuoteEndpoints.Id(experienceId);using var document=await QuoteHttpInput.Read(context.Request,context.RequestAborted,8192);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"outcome","reason");
            return Outcome(context,await service.ReviewExperienceAsync(LocalIdentityService.Actor(context.User),draftId,experienceId,command.Version,command.Lease,
                QuoteReferralEndpoints.Text(root,"outcome",20),QuoteReferralEndpoints.Text(root,"reason",2000),command.Key,Guid.NewGuid(),context.RequestAborted));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static (string Key,byte[] Version,Guid Lease) Command(Guid draftId,HttpContext context)
    {QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);return(QuoteHttpInput.Key(context.Request),QuoteHttpInput.Version(context.Request),ServicingEndpoints.Fence(context.Request));}
    private static IResult Outcome(HttpContext context,CommandOutcome result)
    {context.Response.Headers.ETag=result.Etag;return Results.Content(result.Body,"application/json",statusCode:result.Status);}
    private static int Number(JsonElement root,string name)=>root.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var result)?result:throw new QuoteHttpException(422,"renewal-number-invalid");
    private static DateOnly Day(JsonElement root,string name)=>DateOnly.TryParseExact(QuoteReferralEndpoints.Text(root,name,10),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)?day:throw new QuoteHttpException(422,"renewal-date-invalid");
    private static string Money(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
    private static decimal Amount(JsonElement root,string name)
    {
        var text=QuoteReferralEndpoints.Text(root,name,16);
        if(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) || value<0 || value>QuoteRatingRules.MaximumMoney || text!=Money(value))throw new QuoteHttpException(422,"renewal-amount-invalid");
        return value;
    }
}
