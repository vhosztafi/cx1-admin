using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.AspNetCore.Http.Features;

namespace BackOffice.Api;

public static class ServicingProofCommandEndpoints
{
    public static void MapServicingProofCommands(this WebApplication app)
    {
        app.MapPost("/api/v1/drafts/{draftId:guid}/evidence/uploads",Upload).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/evidence",Attach).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/evidence/{associationId:guid}/reviews",(Guid draftId,Guid associationId,HttpContext c,ServicingEvidenceService s)=>Event(draftId,associationId,true,c,s)).RequireAuthorization("underwriting-evidence-review");
        app.MapPost("/api/v1/drafts/{draftId:guid}/evidence/{associationId:guid}/withdraw",(Guid draftId,Guid associationId,HttpContext c,ServicingEvidenceService s)=>Event(draftId,associationId,false,c,s)).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/referrals/decisions",(Guid draftId,HttpContext c,ServicingReferralService s)=>Decide(draftId,null,c,s)).RequireAuthorization("underwriting-decide-within-authority");
        app.MapPost("/api/v1/drafts/{draftId:guid}/referrals/{referralId:guid}/decisions",(Guid draftId,Guid referralId,HttpContext c,ServicingReferralService s)=>Decide(draftId,referralId,c,s)).RequireAuthorization("underwriting-decide-within-authority");
        app.MapPost("/api/v1/drafts/{draftId:guid}/conditions/{conditionId:guid}/resolutions",Resolve).RequireAuthorization("underwriting-decide-within-authority");
    }

    private static IResult Outcome(HttpContext context,BackOffice.Infrastructure.Platform.CommandOutcome result)
    {
        context.Response.Headers.ETag=result.Etag;
        return Results.Content(result.Body,"application/json",statusCode:result.Status);
    }

    private static (string Key,byte[] Version,Guid Lease) Command(Guid draftId,HttpContext context)
    {
        QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);
        return (QuoteHttpInput.Key(context.Request),QuoteHttpInput.Version(context.Request),ServicingEndpoints.Fence(context.Request));
    }

    private static async Task<IResult> Upload(Guid draftId,HttpContext context,ServicingEvidenceService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);var token=context.RequestAborted;
            if(!context.Request.HasFormContentType || !context.Request.ContentType!.StartsWith("multipart/form-data",StringComparison.OrdinalIgnoreCase))throw new QuoteHttpException(415,"multipart-required");
            const int maximum=QuoteEvidenceRules.MaximumFileBytes+16384;
            if(context.Request.ContentLength>maximum)throw new QuoteHttpException(413,"evidence-file-size");
            using var body=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await context.Request.Body.ReadAsync(buffer,token))>0)
            {if(body.Length+count>maximum)throw new QuoteHttpException(413,"evidence-file-size");body.Write(buffer,0,count);}
            body.Position=0;context.Request.Body=body;
            var form=await context.Request.ReadFormAsync(new FormOptions{MultipartBodyLengthLimit=maximum,ValueLengthLimit=256,ValueCountLimit=8,MultipartHeadersLengthLimit=1024,MultipartHeadersCountLimit=8},token);
            if(form.Files.Count!=1 || form.Files[0].Name!="file" || form.Count!=2 || form.Keys.Any(x=>x is not("fileName" or "contentType")) || form["fileName"].Count!=1 || form["contentType"].Count!=1)
                throw new QuoteHttpException(422,"evidence-upload-fields");
            var upload=form.Files[0];if(upload.Length is 0 or >QuoteEvidenceRules.MaximumFileBytes)throw new QuoteHttpException(413,"evidence-file-size");
            if(upload.FileName!=form["fileName"].ToString() || upload.ContentType!=form["contentType"].ToString())throw new QuoteHttpException(422,"evidence-upload-metadata");
            using var file=new MemoryStream();await upload.CopyToAsync(file,token);
            var result=await service.UploadAsync(LocalIdentityService.Actor(context.User),draftId,command.Version,command.Lease,upload.FileName,upload.ContentType,file.ToArray(),command.Key,Guid.NewGuid(),token);
            context.Response.Headers.Location=$"/api/v1/drafts/{draftId:D}/evidence-files/{result.ResourceId:D}/content";
            return Outcome(context,result);
        }
        catch(InvalidDataException){return IdentityEndpoints.Problem(context,422,"invalid-evidence-upload","Check the upload format and size.");}
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Attach(Guid draftId,HttpContext context,ServicingEvidenceService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","fileId","requirementCode","riskItemId","inputFingerprint","reason");
            return Outcome(context,await service.AttachAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),command.Version,command.Lease,
                QuoteHttpInput.Id(root,"fileId"),QuoteReferralEndpoints.Text(root,"requirementCode",60),root.TryGetProperty("riskItemId",out _)?QuoteHttpInput.Id(root,"riskItemId"):null,
                QuoteReferralEndpoints.Text(root,"inputFingerprint",64),QuoteReferralEndpoints.Text(root,"reason",2000),command.Key,Guid.NewGuid(),token));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Event(Guid draftId,Guid associationId,bool review,HttpContext context,ServicingEvidenceService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);QuoteEndpoints.Id(associationId);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,review?["cycleId","associationEtag","outcome","expectedFingerprint","reason"]:["cycleId","associationEtag","reason"]);
            var actor=LocalIdentityService.Actor(context.User);var cycle=QuoteHttpInput.Id(root,"cycleId");var child=QuoteReferralEndpoints.Version(root,"associationEtag");var reason=QuoteReferralEndpoints.Text(root,"reason",2000);
            var result=review?await service.ReviewAsync(actor,draftId,cycle,associationId,command.Version,command.Lease,child,QuoteReferralEndpoints.Text(root,"outcome",20),QuoteReferralEndpoints.Text(root,"expectedFingerprint",64),reason,command.Key,Guid.NewGuid(),token)
                :await service.WithdrawAsync(actor,draftId,cycle,associationId,command.Version,command.Lease,child,reason,command.Key,Guid.NewGuid(),token);
            return Outcome(context,result);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Decide(Guid draftId,Guid? referralId,HttpContext context,ServicingReferralService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);if(referralId is {} id)QuoteEndpoints.Id(id);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,1048576);var root=document.RootElement;
            QuoteHttpInput.Keys(root,referralId is null?["cycleId","decisions"]:["cycleId","decision"]);
            ReferralDecisionInput[] decisions;
            if(referralId is not null)
            {
                if(!root.TryGetProperty("decision",out var item) || item.ValueKind!=JsonValueKind.Object)throw new QuoteHttpException(422,"referral-decision-required");
                decisions=[QuoteReferralEndpoints.Decision(item)];
            }
            else
            {
                if(!root.TryGetProperty("decisions",out var items) || items.ValueKind!=JsonValueKind.Array || items.GetArrayLength() is <1 or >50 || items.EnumerateArray().Any(x=>x.ValueKind!=JsonValueKind.Object))
                    throw new QuoteHttpException(422,"referral-decisions-required");
                decisions=items.EnumerateArray().Select(QuoteReferralEndpoints.Decision).ToArray();
            }
            return Outcome(context,await service.DecideAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),command.Version,command.Lease,decisions,command.Key,Guid.NewGuid(),token,referralId));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> Resolve(Guid draftId,Guid conditionId,HttpContext context,ServicingReferralService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            var command=Command(draftId,context);QuoteEndpoints.Id(conditionId);var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","conditionEtag","evidenceAssociationId","outcome","reason");
            return Outcome(context,await service.ResolveAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),conditionId,command.Version,command.Lease,
                QuoteReferralEndpoints.Version(root,"conditionEtag"),QuoteHttpInput.Id(root,"evidenceAssociationId"),QuoteReferralEndpoints.Text(root,"outcome",20),QuoteReferralEndpoints.Text(root,"reason",2000),command.Key,Guid.NewGuid(),token));
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
