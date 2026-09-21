using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using Microsoft.Net.Http.Headers;
using BackOffice.Application.Operations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Api;

public static class DocumentEndpoints
{
    private static readonly JsonSerializerOptions InputJson = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive=false, UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };

    public static void MapDocuments(this WebApplication app)
    {
        app.MapGet("/api/v1/records/{recordId:guid}/documents",(Guid recordId,HttpContext context,DocumentService service,PartyPaging paging)=>List(recordId,context,service,paging,false)).RequireAuthorization("document-read");
        app.MapGet("/api/v1/documents/{documentId:guid}/versions",(Guid documentId,HttpContext context,DocumentService service,PartyPaging paging)=>List(documentId,context,service,paging,true)).RequireAuthorization("document-read");
        app.MapPost("/api/v1/records/{recordId:guid}/documents/generate",(Guid recordId,HttpContext context,DocumentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);
            var key=QuoteHttpInput.Key(context.Request);
            using var body=await QuoteHttpInput.Read(context.Request,context.RequestAborted,65536);
            DocumentGenerateInput input;
            try { input=body.RootElement.Deserialize<DocumentGenerateInput>(InputJson) ?? throw new QuoteHttpException(400,"invalid-document-input"); }
            catch(JsonException) { throw new QuoteHttpException(400,"invalid-document-input"); }
            var result=await service.Generate(LocalIdentityService.Actor(context.User),recordId,input,key,context.RequestAborted);
            context.Response.Headers.Location="/api/v1/document-versions/"+result.ResourceId;
            return QuoteEndpoints.Outcome(context,result);
        })).RequireAuthorization("document-generate");
        app.MapGet("/api/v1/document-versions/{versionId:guid}",(Guid versionId,HttpContext context,DocumentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(versionId);QuoteHttpInput.NoQuery(context.Request);
            return QuoteEndpoints.Outcome(context,await service.ReadVersion(LocalIdentityService.Actor(context.User),versionId,context.RequestAborted));
        })).RequireAuthorization("document-read");
        app.MapGet("/api/v1/document-versions/{versionId:guid}/content",(Guid versionId,HttpContext context,DocumentService service)=>Content(versionId,context,service,false)).RequireAuthorization("document-download");
        app.MapGet("/api/v1/document-versions/{versionId:guid}/preview",(Guid versionId,HttpContext context,DocumentService service)=>Content(versionId,context,service,true)).RequireAuthorization("document-download");
    }
    private static Task<IResult> List(Guid id,HttpContext context,DocumentService service,PartyPaging paging,bool versions)=>Run(context,async()=>
    {
        QuoteEndpoints.Id(id);var actor=LocalIdentityService.Actor(context.User);
        var page=paging.Read(context,actor,versions?"document-versions-sequence-v1":"documents-created-keyset-v1") ?? throw new QuoteHttpException(400,"invalid-document-cursor");
        var result=versions?await service.ListVersions(actor,id,page.Offset,page.Size,page.AsOf,context.RequestAborted)
            :await service.ListDocuments(actor,id,page.KeyId,page.Size,page.AsOf,context.RequestAborted);
        var next=versions?paging.NextKeyset(page,result.NextNumber):paging.NextGuid(page,result.NextDocumentId);
        return Results.Json(new{items=result.Items,totalCount=result.TotalCount,nextCursor=next},ClientEndpoints.Json);
    });
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
        catch(DocumentRuleException error){return IdentityEndpoints.Problem(context,422,error.Code,"Check the selected document details.");}
        catch(DocumentRenderException error){return IdentityEndpoints.Problem(context,422,error.Code,"The selected source or template cannot produce this document.");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return IdentityEndpoints.Problem(context,503,"document-storage-unavailable","The document is unavailable. Try again later.");}
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
