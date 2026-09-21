using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using Microsoft.Net.Http.Headers;

namespace BackOffice.Api;

public static class DocumentEndpoints
{
    public static void MapDocuments(this WebApplication app)
    {
        app.MapGet("/api/v1/document-versions/{versionId:guid}",(Guid versionId,HttpContext context,DocumentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(versionId);QuoteHttpInput.NoQuery(context.Request);
            return QuoteEndpoints.Outcome(context,await service.ReadVersion(LocalIdentityService.Actor(context.User),versionId,context.RequestAborted));
        })).RequireAuthorization("document-read");
        app.MapGet("/api/v1/document-versions/{versionId:guid}/content",(Guid versionId,HttpContext context,DocumentService service)=>Content(versionId,context,service,false)).RequireAuthorization("document-download");
        app.MapGet("/api/v1/document-versions/{versionId:guid}/preview",(Guid versionId,HttpContext context,DocumentService service)=>Content(versionId,context,service,true)).RequireAuthorization("document-download");
    }
    private static Task<IResult> Content(Guid id,HttpContext context,DocumentService service,bool preview)=>Run(context,async()=>
    {
        QuoteEndpoints.Id(id);QuoteHttpInput.NoQuery(context.Request);
        var file=await service.DownloadVersion(LocalIdentityService.Actor(context.User),id,context.RequestAborted);
        if(preview)
        {
            var header=new ContentDispositionHeaderValue("inline");header.SetHttpFileName(file.Name);context.Response.GetTypedHeaders().ContentDisposition=header;
            return Results.Stream(file.Content,file.MediaType,enableRangeProcessing:false);
        }
        return Results.Stream(file.Content,file.MediaType,file.Name,enableRangeProcessing:false);
    });
    private static async Task<IResult> Run(HttpContext context,Func<Task<IResult>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";context.Response.Headers.XContentTypeOptions="nosniff";
        try{return await action();}
        catch(OperationalAccessException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"The document is unavailable.");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return IdentityEndpoints.Problem(context,503,"document-storage-unavailable","The document is unavailable. Try again later.");}
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
