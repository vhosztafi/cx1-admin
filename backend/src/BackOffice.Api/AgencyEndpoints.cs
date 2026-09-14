using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyEndpoints
{
    private static readonly JsonSerializerOptions Json=ClientEndpoints.Json;
    public static void MapAgencies(this WebApplication app)
    {
        app.MapGet("/api/v1/agencies",List).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/kpis",Kpis).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/{agencyId:guid}",Get).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/products",Products).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/activity",Activity).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agency-relationship-managers",Managers).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agency-product-catalog",Catalog).RequireAuthorization("agency-read");
        app.MapPost("/api/v1/agencies",(HttpContext context,AgencyDraftService service)=>Save(null,context,service)).RequireAuthorization("agency-admin");
        app.MapPut("/api/v1/agencies/{agencyId:guid}",(Guid agencyId,HttpContext context,AgencyDraftService service)=>Save(agencyId,context,service)).RequireAuthorization("agency-admin");
        app.MapPut("/api/v1/agencies/{agencyId:guid}/products",ReplaceProducts).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/abandon",Abandon).RequireAuthorization("agency-admin");
    }
    private static async Task<IResult> Save(Guid? agencyId,HttpContext context,AgencyDraftService service)
    {
        try
        {
            var input=ClientEndpoints.Input<DraftInput>(await ReadBody(context));var draft=AgencyDraftRules.Validate(input.Details);
            var result=await service.Save(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),agencyId is null?null:ClientEndpoints.Version(context),draft,input.OnboardingStep,input.Products,context.RequestAborted);
            return Response(context,result);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> ReplaceProducts(Guid agencyId,HttpContext context,AgencyDraftService service)
    {
        try{var input=ClientEndpoints.Input<ProductsInput>(await ReadBody(context));return Response(context,await service.ReplaceProducts(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Products,input.Reason,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Abandon(Guid agencyId,HttpContext context,AgencyDraftService service)
    {
        try{var input=ClientEndpoints.Input<ReasonInput>(await ReadBody(context));return Response(context,await service.Abandon(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Reason,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    internal static async Task<JsonElement> ReadBody(HttpContext context)
    {
        if(!context.Request.HasJsonContentType())throw new AgencyCommandException(415,"json-required");
        if(context.Request.ContentLength>65536)throw new AgencyCommandException(413,"agency-draft-too-large");
        using var stream=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0){if(stream.Length+count>65536)throw new AgencyCommandException(413,"agency-draft-too-large");stream.Write(buffer,0,count);}
        using var parsed=JsonDocument.Parse(stream.ToArray(),new JsonDocumentOptions{MaxDepth=16});return parsed.RootElement.Clone();
    }
    private static async Task<IResult> Get(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyEvidenceService evidence)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,context.RequestAborted);
        var agency=await db.Set<Agency>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==agencyId,context.RequestAborted);if(agency is null)return Missing(context);
        var draft=await db.Set<AgencyOnboarding>().AsNoTracking().SingleOrDefaultAsync(x=>x.AgencyId==agencyId,context.RequestAborted);
        using var doc=JsonDocument.Parse(draft?.Details??JsonSerializer.Serialize(new{legalName=agency.LegalName}));
        var validation=await evidence.Validate(db,agency,context.RequestAborted);
        var userCount=await BrokerUsers(db).CountAsync(x=>x.AgencyId==agencyId,context.RequestAborted);
        var invitedUserCount=await BrokerUsers(db).CountAsync(x=>x.AgencyId==agencyId&&x.State=="invited",context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);
        context.Response.Headers.ETag=AgencyDraftService.Etag(agency.RowVersion);
        return Results.Json(new{agency.Id,agency.Reference,agency.State,agency.OnboardingStep,userCount,invitedUserCount,details=doc.RootElement.Clone(),validation,unavailableSections=new[]{new{kind="quotes",state="unavailable",owningPhase=5,message="Quote capture is not available yet."},new{kind="policies",state="unavailable",owningPhase=6,message="Policy records are not available yet."},new{kind="tasks",state="unavailable",owningPhase=9,message="Agency tasks are not available yet."},new{kind="statements",state="unavailable",owningPhase=10,message="Statements are not available yet."}}},Json);
    }
    private static async Task<IResult> List(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var page=paging.Read(context,LocalIdentityService.Actor(context.User),"reference,id","q","state","relationshipManagerId");
        var search=context.Request.Query["q"].ToString();var state=context.Request.Query["state"].ToString();var manager=context.Request.Query["relationshipManagerId"].ToString();
        if(page is null||search.Length>200||search.Any(char.IsControl)||(search.Length>0&&string.IsNullOrWhiteSpace(search))||(state.Length>0&&state is not("draft" or "active" or "suspended" or "abandoned"))||(manager.Length>0&&!Guid.TryParseExact(manager,"D",out _)))return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var query=db.Set<Agency>().AsNoTracking().Where(x=>x.CreatedAt<=page.AsOf);
        if(search.Length>0){var normalized=ClientIdentity.NormalizeName(search);query=query.Where(x=>x.NormalizedName.Contains(normalized)||x.Reference.Contains(normalized));}
        if(state.Length>0)query=query.Where(x=>x.State==state);if(manager.Length>0){var id=Guid.Parse(manager);query=query.Where(x=>x.RelationshipManagerId==id);}
        var total=await query.CountAsync(context.RequestAborted);var rows=await query.OrderBy(x=>x.Reference).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        var ids=rows.Select(x=>x.Id).ToArray();var managers=await db.Set<StaffUser>().Where(x=>rows.Select(r=>r.RelationshipManagerId).Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,context.RequestAborted);
        var products=await(from grant in db.Set<AgencyDraftProduct>() join v in db.Set<ProductVersion>() on grant.ProductVersionId equals v.Id join p in db.Set<Product>() on v.ProductId equals p.Id where ids.Contains(grant.AgencyId) select new{grant.AgencyId,p.Code}).ToListAsync(context.RequestAborted);
        var activity=await db.Set<AgencyActivity>().Where(x=>ids.Contains(x.AgencyId)).GroupBy(x=>x.AgencyId).Select(g=>new{Id=g.Key,Last=g.Max(x=>x.OccurredAt)}).ToDictionaryAsync(x=>x.Id,x=>x.Last,context.RequestAborted);
        var drafts=await db.Set<AgencyOnboarding>().AsNoTracking().Where(x=>ids.Contains(x.AgencyId)).Select(x=>new{x.AgencyId,x.Details}).ToListAsync(context.RequestAborted);
        var userCounts=await BrokerUsers(db).Where(x=>ids.Contains(x.AgencyId!.Value)).GroupBy(x=>x.AgencyId!.Value).Select(g=>new{Id=g.Key,Total=g.Count(),Invited=g.Count(x=>x.State=="invited")}).ToDictionaryAsync(x=>x.Id,context.RequestAborted);
        var contacts=drafts.ToDictionary(x=>x.AgencyId,x=>MainContactName(x.Details));
        return Results.Json(new{items=rows.Select(x=>new{x.Id,x.Reference,legalName=x.LegalName.Length>0?x.LegalName:null,x.State,x.OnboardingStep,userCount=userCounts.GetValueOrDefault(x.Id)?.Total??0,invitedUserCount=userCounts.GetValueOrDefault(x.Id)?.Invited??0,x.RelationshipManagerId,mainContactName=contacts.GetValueOrDefault(x.Id),relationshipManagerName=x.RelationshipManagerId is Guid m?managers.GetValueOrDefault(m):null,productCodes=products.Where(p=>p.AgencyId==x.Id).Select(p=>p.Code),lastActivityAt=activity.TryGetValue(x.Id,out var at)?(DateTimeOffset?)at:null}),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},Json);
    }
    private static string? MainContactName(string details)
    {
        using var document=JsonDocument.Parse(details);
        return document.RootElement.TryGetProperty("mainContact",out var contact)&&contact.ValueKind==JsonValueKind.Object&&contact.TryGetProperty("name",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString():null;
    }
    // Retained agency identities, including inactive users; invited is a subset,
    // counting staged/pending acceptance identities rather than delivery attempts.
    private static IQueryable<StaffUser> BrokerUsers(BackOfficeDbContext db)=>db.Set<StaffUser>().Where(user=>user.AgencyId!=null&&db.Set<UserRole>().Any(link=>link.UserId==user.Id&&db.Set<Role>().Any(role=>role.Id==link.RoleId&&role.Scope=="agency"&&(role.Code=="broker-admin"||role.Code=="broker-user"||role.Code=="broker-readonly"))));
    private static async Task<IResult> Kpis(IDbContextFactory<BackOfficeDbContext> factory,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);var counts=await db.Set<Agency>().GroupBy(x=>x.State).Select(x=>new{State=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.State,x=>x.Count,token);
        var users=await BrokerUsers(db).GroupBy(x=>1).Select(g=>new{Total=g.Count(),Invited=g.Count(x=>x.State=="invited")}).SingleOrDefaultAsync(token);
        return Results.Json(new{brokerUsers=users?.Total??0,invitedUsers=users?.Invited??0,active=counts.GetValueOrDefault("active"),onboarding=counts.GetValueOrDefault("draft"),suspended=counts.GetValueOrDefault("suspended")});
    }
    private static async Task<IResult> Products(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
    {
        if(context.Request.Query.Count>0)return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);await using var transaction=await db.Database.BeginTransactionAsync(context.RequestAborted);
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleOrDefaultAsync(context.RequestAborted);if(agency is null)return Missing(context);
        object items;
        if(agency.State is "draft" or "abandoned")
            items=await(from grant in db.Set<AgencyDraftProduct>() join v in db.Set<ProductVersion>() on grant.ProductVersionId equals v.Id join p in db.Set<Product>() on v.ProductId equals p.Id where grant.AgencyId==agencyId orderby p.Code select new{grant.Id,grant.ProductVersionId,grant.EffectiveFrom,grant.BrokerCommissionBasisPoints,productCode=p.Code}).ToListAsync(context.RequestAborted);
        else
        {
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            var current=await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==agencyId&&x.EffectiveFrom<=today).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Version).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(context.RequestAborted);
            var end=await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==agencyId&&x.EffectiveFrom>today).MinAsync(x=>(DateOnly?)x.EffectiveFrom,context.RequestAborted);
            items=await(from grant in db.Set<AgencyProduct>() join v in db.Set<ProductVersion>() on grant.ProductVersionId equals v.Id join p in db.Set<Product>() on v.ProductId equals p.Id where grant.AgencyTermsVersionId==current&&grant.EffectiveFrom<=today orderby p.Code select new{grant.Id,grant.ProductVersionId,grant.EffectiveFrom,grant.BrokerCommissionBasisPoints,productCode=p.Code,termsVersionId=grant.AgencyTermsVersionId,effectiveTo=end}).ToListAsync(context.RequestAborted);
        }
        context.Response.Headers.ETag=AgencyDraftService.Etag(agency.RowVersion);await transaction.CommitAsync(context.RequestAborted);return Results.Json(new{items},Json);
    }
    private static async Task<IResult> Managers(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var page=paging.Read(context,LocalIdentityService.Actor(context.User),"displayName,id");if(page is null)return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var query=AgencyDraftService.Managers(db).Where(x=>x.CreatedAt<=page.AsOf);var total=await query.CountAsync(context.RequestAborted);
        var items=await query.OrderBy(x=>x.DisplayName).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).Select(x=>new{x.Id,x.DisplayName}).ToListAsync(context.RequestAborted);return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},Json);
    }
    private static async Task<IResult> Catalog(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
    {
        if(context.Request.Query.Count>0)return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var items=await(from v in db.Set<ProductVersion>() join p in db.Set<Product>() on v.ProductId equals p.Id join c in db.Set<CapacityProvider>() on v.ProviderId equals c.Id
            where p.Code=="motor-trade-road-risks"||p.Code=="motor-trade-combined"||p.Code=="commercial-combined" orderby p.Code,v.Version descending select new{productVersionId=v.Id,productCode=p.Code,p.Name,capacityProviderName=c.Name,v.EffectiveFrom,v.EffectiveTo,providerState=c.State}).ToListAsync(context.RequestAborted);
        var now=time.GetUtcNow();var rule=await AgencyDistributionService.Configuration(db,now,context.RequestAborted);
        return Results.Json(new{items=items.DistinctBy(x=>x.productCode).Select(x=>new{x.productVersionId,x.productCode,x.Name,x.capacityProviderName,
            distributionEligible=rule?.ProductVersionIds.Contains(x.productVersionId)==true&&x.providerState=="active"&&x.EffectiveFrom<=now&&(x.EffectiveTo is null||x.EffectiveTo>now),
            ratingReady=false,unavailableReason="Rating and policy issue are unavailable for these draft definitions. Distribution eligibility uses a fictional demo rule; agency approval is still required."})},Json);
    }
    private static async Task<IResult> Activity(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);if(!await db.Set<Agency>().AnyAsync(x=>x.Id==agencyId,context.RequestAborted))return Missing(context);
        var page=paging.Read(context,LocalIdentityService.Actor(context.User),"occurredAt-desc,id");if(page is null)return BadQuery(context);
        var query=db.Set<AgencyActivity>().Where(x=>x.AgencyId==agencyId&&x.OccurredAt<=page.AsOf);var total=await query.CountAsync(context.RequestAborted);
        var items=await(from item in query join actor in db.Set<StaffUser>() on item.ActorId equals actor.Id into actors from actor in actors.DefaultIfEmpty() orderby item.OccurredAt descending,item.Id select new{item.Id,item.OccurredAt,actorLabel=actor==null?"System":actor.DisplayName,item.Action,summary=item.Action=="agency.created"?"Agency draft created.":item.Action=="agency.abandoned"?"Agency draft abandoned.":item.Action=="agency.products-saved"?"Draft products saved.":item.Action=="agency.evidence-file-uploaded"?"Evidence file uploaded (demo screening).":item.Action=="agency.evidence-recorded"?"Evidence attestation recorded.":item.Action=="agency.check-completed"?"Demo compliance check completed.":item.Action=="agency.notification-retry"?"Demo notification retry queued.":item.Action=="agency.notification-queued"?"Demo notification queued.":item.Action=="agency.notification-delivered"?"Demo notification delivered.":item.Action=="agency.notification-rejected"?"Demo notification rejected.":item.Action=="agency.user-staged"?"Agency user staged without delivery.":item.Action=="agency.user-created"?"Agency user created.":item.Action=="agency.user-edit"?"Agency user updated.":item.Action=="agency.user-deactivate"?"Agency user deactivated.":item.Action=="agency.user-reactivate"?"Agency user reactivated.":item.Action=="agency.invitation-issued"?"Invitation issued for demo delivery.":item.Action=="agency.invitation-resent"?"Replacement invitation issued.":item.Action=="agency.invitation-revoked"?"Invitation revoked.":item.Action=="agency.invitation-accepted"?"Invitation accepted and password set.":item.Action=="agency.activated"?"Agency activated after independent review.":item.Action=="agency.activation-rejected"?"Activation request rejected.":item.Action=="agency.suspended"?"Agency suspended and access revoked.":item.Action=="agency.suspension-rejected"?"Suspension request rejected.":item.Action=="agency.reactivated"?"Agency reactivated after independent review.":item.Action=="agency.reactivation-rejected"?"Reactivation request rejected.":item.Action=="agency.terms-applied"?"Agreed terms published after independent review.":item.Action=="agency.terms-rejected"?"Agreed terms request rejected.":"Onboarding draft saved."}).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},Json);
    }
    private static bool IsError(Exception ex)=>ex is AgencyCommandException||ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context,Exception ex)=>ex is AgencyCommandException agency?IdentityEndpoints.Problem(context,agency.Status,agency.Code,"Check the agency request and its current state."):ClientEndpoints.CommandError(context,ex,"agency");
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"agency-not-found","Agency not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the list and use supported filters.");
    private static IResult Response(HttpContext context,CommandOutcome result){context.Response.Headers.ETag=result.Etag;if(result.Status==201)context.Response.Headers.Location="/api/v1/agencies/"+result.ResourceId;return Results.Content(result.Body,"application/json",statusCode:result.Status);}
    private sealed record DraftInput([property:JsonRequired]JsonElement Details,[property:JsonRequired]int OnboardingStep,List<AgencyProductInput>? Products=null);
    private sealed record ProductsInput([property:JsonRequired]List<AgencyProductInput> Products,[property:JsonRequired]string Reason);
    private sealed record ReasonInput([property:JsonRequired]string Reason);
}
