using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyEvidenceEndpoints
{
    public static void MapAgencyEvidence(this WebApplication app)
    {
        app.MapPost("/api/v1/agencies/{agencyId:guid}/evidence-files",Upload).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/evidence",Attest).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/checks",Check).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/validate",Validate).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/evidence-files/{fileId:guid}/content",Download).RequireAuthorization("agency-read");
        foreach(var kind in new[]{"evidence-files","evidence","checks"})
        {
            var listKind=kind;
            app.MapGet($"/api/v1/agencies/{{agencyId:guid}}/{kind}",(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>List(agencyId,listKind,null,context,factory,paging)).RequireAuthorization("agency-read");
            app.MapGet($"/api/v1/agencies/{{agencyId:guid}}/{kind}/{{recordId:guid}}",(Guid agencyId,Guid recordId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>List(agencyId,listKind,recordId,context,factory,paging)).RequireAuthorization("agency-read");
        }
    }
    private static async Task<IResult> Upload(Guid agencyId,HttpContext context,AgencyEvidenceService service)
    {
        try
        {
            if(!context.Request.HasFormContentType||!context.Request.ContentType!.StartsWith("multipart/form-data",StringComparison.OrdinalIgnoreCase))throw new AgencyCommandException(415,"multipart-required");
            using var buffer=await ReadBounded(context,AgencyEvidenceRules.MaximumFileBytes+16384);
            context.Request.Body=buffer;
            var form=await context.Request.ReadFormAsync(new FormOptions{MultipartBodyLengthLimit=AgencyEvidenceRules.MaximumFileBytes,ValueLengthLimit=256,ValueCountLimit=3,MultipartHeadersLengthLimit=1024,MultipartHeadersCountLimit=8},context.RequestAborted);
            if(form.Files.Count!=1||form.Files[0].Name!="file"||form.Count!=2||form.Keys.Any(x=>x is not ("fileName" or "contentType"))||form["fileName"].Count!=1||form["contentType"].Count!=1)throw new AgencyCommandException(422,"evidence-upload-fields");
            var upload=form.Files[0];if(upload.Length>AgencyEvidenceRules.MaximumFileBytes)throw new AgencyCommandException(413,"evidence-file-size");
            if(upload.FileName!=form["fileName"].ToString()||upload.ContentType!=form["contentType"].ToString())throw new AgencyCommandException(422,"evidence-upload-metadata");
            using var file=new MemoryStream();await upload.CopyToAsync(file,context.RequestAborted);
            var input=AgencyEvidenceRules.ValidateFile(upload.FileName,upload.ContentType,file.ToArray());
            return Response(context,agencyId,"evidence-files",await service.Upload(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input,context.RequestAborted));
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Attest(Guid agencyId,HttpContext context,AgencyEvidenceService service)
    {
        try{var input=ClientEndpoints.Input<AttestationInput>(await JsonBody(context));return Response(context,agencyId,"evidence",await service.Attest(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Kind,input.FileId,input.Notes,input.ExpiresOn,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Check(Guid agencyId,HttpContext context,AgencyEvidenceService service)
    {
        try{var input=ClientEndpoints.Input<CheckInput>(await JsonBody(context));return Response(context,agencyId,"checks",await service.Check(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Kind,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Validate(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyEvidenceService service)
    {
        try
        {
            if(context.Request.ContentLength is >0||context.Request.Headers.ContainsKey("Transfer-Encoding"))throw new AgencyCommandException(400,"validation-has-no-body");
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,context.RequestAborted);
            var agency=await db.Set<Agency>().SingleOrDefaultAsync(x=>x.Id==agencyId,context.RequestAborted);if(agency is null)return Missing(context);
            if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(agency.RowVersion,ClientEndpoints.Version(context)))throw new AgencyCommandException(412,"stale-agency");
            var result=await service.Validate(db,agency,context.RequestAborted);await transaction.CommitAsync(context.RequestAborted);context.Response.Headers.ETag=result.AgencyEtag;
            return Results.Json(result,ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Download(Guid agencyId,Guid fileId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var file=await db.Set<AgencyEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==fileId&&x.AgencyId==agencyId&&x.ScreeningState=="demo-cleared",context.RequestAborted);if(file is null)return Missing(context);
        context.Response.Headers.CacheControl="no-store";context.Response.Headers.XContentTypeOptions="nosniff";
        return Results.File(file.Content,file.ContentType,fileDownloadName:file.FileName,enableRangeProcessing:false);
    }
    private static async Task<IResult> List(Guid agencyId,string kind,Guid? recordId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        if(!await db.Set<Agency>().AnyAsync(x=>x.Id==agencyId,context.RequestAborted))return Missing(context);
        var page=paging.Read(context,LocalIdentityService.Actor(context.User),"createdAt-desc,id");if(page is null)return IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the evidence list.");
        IQueryable<object> query=kind switch
        {
            "evidence-files"=>db.Set<AgencyEvidenceFile>().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf&&(recordId==null||x.Id==recordId)).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Select(x=>new{x.Id,x.AgencyId,x.FileName,x.ContentType,x.ByteLength,x.Sha256,x.ScreeningState,uploadedAt=x.CreatedAt}),
            "evidence"=>db.Set<AgencyEvidence>().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf&&(recordId==null||x.Id==recordId)).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Select(x=>new{x.Id,x.AgencyId,x.Kind,x.State,x.FileId,x.InputFingerprint,x.RuleVersionId,x.AttestedBy,recordedAt=x.CreatedAt,x.VerifiedAt,x.ExpiresOn,x.ResultCode,x.Notes}),
            _=>db.Set<AgencyCheckAttempt>().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf&&(recordId==null||x.Id==recordId)).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Select(x=>new{x.Id,x.AgencyId,x.Kind,x.State,x.InputFingerprint,x.RuleVersionId,x.CreatedAt,x.CompletedAt,x.ResultCode,x.EvidenceId})
        };
        var total=await query.CountAsync(context.RequestAborted);var items=await query.Skip(recordId is null?page.Offset:0).Take(recordId is null?page.Size:1).ToListAsync(context.RequestAborted);
        context.Response.Headers.CacheControl="no-store";
        return recordId is not null?items.Count==0?Missing(context):Results.Json(items[0],ClientEndpoints.Json):Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<JsonElement> JsonBody(HttpContext context){if(!context.Request.HasJsonContentType())throw new AgencyCommandException(415,"json-required");using var body=await ReadBounded(context,8192);using var document=await JsonDocument.ParseAsync(body,new JsonDocumentOptions{MaxDepth=8},context.RequestAborted);return document.RootElement.Clone();}
    private static async Task<MemoryStream> ReadBounded(HttpContext context,int maximum)
    {
        if(context.Request.ContentLength>maximum)throw new AgencyCommandException(413,"evidence-body-size");var output=new MemoryStream();
        try{var bytes=new byte[8192];int read;while((read=await context.Request.Body.ReadAsync(bytes,context.RequestAborted))>0){if(output.Length+read>maximum)throw new AgencyCommandException(413,"evidence-body-size");output.Write(bytes,0,read);}output.Position=0;return output;}
        catch{output.Dispose();throw;}
    }
    private static IResult Response(HttpContext context,Guid agency,string kind,CommandOutcome outcome){context.Response.Headers.ETag=outcome.Etag;context.Response.Headers.Location=$"/api/v1/agencies/{agency}/{kind}/{outcome.ResourceId}";return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);}
    private static bool IsError(Exception ex)=>ex is AgencyCommandException or InvalidDataException||ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context,Exception ex)=>ex is AgencyCommandException error?IdentityEndpoints.Problem(context,error.Status,error.Code,"Check the evidence request and current agency version."):ex is InvalidDataException?IdentityEndpoints.Problem(context,422,"invalid-upload","Check the upload size and format."):ClientEndpoints.CommandError(context,ex,"agency evidence");
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"agency-evidence-not-found","Agency evidence not found.");
    private sealed record CheckInput([property:JsonRequired]string Kind);
    private sealed record AttestationInput([property:JsonRequired]string Kind,[property:JsonRequired]Guid FileId,[property:JsonRequired]string Notes,DateOnly? ExpiresOn=null);
}
