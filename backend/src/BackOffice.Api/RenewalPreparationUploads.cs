using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Http.Features;

namespace BackOffice.Api;

public static partial class RenewalPreparationEndpoints
{
    private static async Task<IResult> Upload(Guid draftId,HttpContext context,RenewalPreparationService service)
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
            return Outcome(context,await service.UploadExperienceAsync(LocalIdentityService.Actor(context.User),draftId,command.Version,command.Lease,
                upload.FileName,upload.ContentType,file.ToArray(),command.Key,Guid.NewGuid(),token));
        }
        catch(InvalidDataException){return IdentityEndpoints.Problem(context,422,"invalid-evidence-upload","Check the upload format and size.");}
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
