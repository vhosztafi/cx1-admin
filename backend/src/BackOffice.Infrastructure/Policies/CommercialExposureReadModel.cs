using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class CommercialExposureReadModel(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private const int MaximumRows = 1000;
    private sealed record Source(string Kind, Guid Id, string Hash);

    public async Task<Dictionary<string, object>> ReadAsync(ActorContext actor, string subjectKind, Guid subjectId,
        DateTimeOffset? effectiveAt = null, DateTimeOffset? knownAt = null, CancellationToken token = default)
    {
        if (subjectId == Guid.Empty || subjectKind is not ("quotes" or "policies" or "drafts")) throw new QuoteOperationException(400, "invalid-commercial-exposure-query");
        if (actor.AgencyId is null && !actor.HasCapability("policy-read")) throw Denied();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Shared observation fence has the same order as exclusive issue. No
        // source, identity or parent row lock is retained before this acquisition.
        await CommercialExposureLock.ReadAsync(db, token);
        var observed = time.GetUtcNow(); var effective = (effectiveAt ?? observed).ToUniversalTime(); var known = (knownAt ?? observed).ToUniversalTime();
        if (known > observed || effective.Year >= 9999) throw new QuoteOperationException(400, "invalid-commercial-exposure-query");
        Guid quoteId;
        ServicingDraft? draftHint = null; Policy? policyHint = null;
        if (subjectKind == "quotes") quoteId = subjectId;
        else
        {
            var policyId = subjectId;
            if (subjectKind == "drafts")
            {
                draftHint = await db.Set<ServicingDraft>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == subjectId, token) ?? throw Missing();
                policyId = draftHint.PolicyId;
            }
            policyHint = await db.Set<Policy>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId, token) ?? throw Missing();
            quoteId = policyHint.SourceQuoteId;
        }
        var owned = await HoldQuote(db, actor, quoteId, token);
        var internalAudience = owned.Scope.Actor.AgencyId is null && owned.Scope.Actor.HasCapability("underwriting-read");
        if (!await db.Set<Product>().AnyAsync(x => x.Id == owned.Quote.ProductId && x.Code == CommercialCaptureRules.ProductCode, token))
            throw new QuoteOperationException(409, "commercial-exposure-product-required");
        Dictionary<string, object> result;
        if (subjectKind != "quotes" || owned.Quote.BoundPolicyId is not null)
        {
            var policyId = policyHint?.Id ?? owned.Quote.BoundPolicyId!.Value;
            var policy = await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(HOLDLOCK) WHERE Id={policyId}").AsNoTracking().SingleAsync(token);
            if (owned.Quote.BoundPolicyId != policy.Id || policy.SourceQuoteId != owned.Quote.Id || policy.ClientId != owned.Quote.ClientId ||
                policy.RelationshipId != owned.Quote.RelationshipId || policy.AgencyId != owned.Quote.AgencyId) throw Missing();
            if (draftHint is not null)
            {
                var draft = await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(HOLDLOCK) WHERE Id={subjectId}").AsNoTracking().SingleAsync(token);
                if (draft.PolicyId != policy.Id || !await db.Set<PolicyVersion>().AnyAsync(x => x.Id == draft.BaseVersionId && x.PolicyId == policy.Id && x.TermId == draft.BaseTermId, token)) throw Missing();
                var revision = await db.Set<ServicingRevision>().AsNoTracking().Where(x => x.DraftId == draft.Id && x.CreatedAt <= known).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(token);
                result = await Draft(db,draft,revision,internalAudience,observed,effective,known,token);
            }
            else result = await Policy(db, policy.Id, internalAudience, observed, effective, known, token);
        }
        else result = await Quote(db, owned.Quote, internalAudience, observed, effective, known, token);
        await transaction.CommitAsync(token); return result;
    }

    private static async Task<Dictionary<string, object>> Draft(BackOfficeDbContext db,ServicingDraft draft,ServicingRevision? revision,
        bool internalAudience,DateTimeOffset observed,DateTimeOffset effective,DateTimeOffset known,CancellationToken token)
    {
        if(draft.IssuedTransactionId is {} issued && await db.Set<PolicyTransaction>().AnyAsync(x=>x.Id==issued && x.ProcessedAt<=known,token))
            return await Policy(db,draft.PolicyId,internalAudience,observed,effective,known,token);
        var source=revision is null?null:new Source("draft-revision",revision.Id,Convert.ToHexStringLower(revision.ContentHash));
        Dictionary<string,object> Unavailable(string code)=>Body(internalAudience,observed,effective,known,"unavailable",null,[],source,code);
        if(revision is null || draft.Kind is not("adjustment" or "renewal"))return Unavailable("commercial-exposure-source-unavailable");
        var renewal=draft.Kind=="renewal";
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==draft.BaseVersionId && x.ProcessedAt<=known,token);
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==draft.BaseTermId,token);
        if(basis is null)return Unavailable("commercial-exposure-source-unavailable");
        var latest=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.TermId==term.Id && x.ProcessedAt<=known)
            .OrderByDescending(x=>x.EffectiveAt).ThenByDescending(x=>x.Sequence).Select(x=>x.Id).FirstAsync(token);
        if(latest!=basis.Id)return Unavailable("commercial-exposure-source-unavailable");
        var book=await db.Set<CommercialExposureVersion>().AsNoTracking().Where(x=>x.VersionId==basis.Id).Select(x=>(Guid?)x.BookId).SingleOrDefaultAsync(token);
        RenewalPreparationVersion? preparation=null;
        if(renewal)
        {
            preparation=await db.Set<RenewalPreparationVersion>().AsNoTracking().Where(x=>x.DraftId==draft.Id&&x.CreatedAt<=known).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
            if(preparation is null)return Unavailable("commercial-exposure-proposal-incomplete");
            book=await db.Set<CommercialExposureBinder>().AsNoTracking().Where(x=>x.BinderVersionId==preparation.BinderVersionId).Select(x=>(Guid?)x.BookId).SingleOrDefaultAsync(token);
        }
        if(book is null)return Unavailable("commercial-exposure-book-unavailable");
        try
        {
            // Assess the retained saved revision at its own clock. This read is
            // advisory; it grants no backdating or reservation authority.
            ResolvedQuoteTerm? renewalTerm=null;
            if(preparation is not null)
            {
                using var intent=JsonDocument.Parse(preparation.TermIntentJson);renewalTerm=QuoteTerm.Assess(intent.RootElement).Term;
                if(renewalTerm is null||renewalTerm.StartsAt!=preparation.StartsAt||renewalTerm.EndsAt!=preparation.EndsAt)return Unavailable("commercial-exposure-proposal-incomplete");
            }
            var assessment=CommercialServicingProposalRules.Assess(basis.SnapshotJson,revision.ProposalJson,renewalTerm is {} coverage
                ?new(draft.PolicyId,basis.Id,coverage.StartsAt,coverage.EndsAt,coverage.StartsAt,revision.CreatedAt,false,coverage)
                :new(draft.PolicyId,basis.Id,term.StartsAt,term.EndsAt,basis.EffectiveAt,revision.CreatedAt,true));
            if(assessment.ReadinessIssues.Count!=0 || (!renewal&&assessment.Slices.Count==0) || (renewal&&assessment.Slices.Any(x=>x.EffectiveAt!=renewalTerm!.StartsAt)))return Unavailable("commercial-exposure-proposal-incomplete");
            var sequence=checked((await db.Set<PolicyTransaction>().Where(x=>x.TermId==term.Id && x.ProcessedAt<=known).MaxAsync(x=>(int?)x.Sequence,token)??0)+1);
            var proposed=renewal?[new CommercialExposureSlice(book.Value,draft.PolicyId,Guid.NewGuid(),Guid.NewGuid(),renewalTerm!.StartsAt,renewalTerm.EndsAt,
                renewalTerm.StartsAt,revision.CreatedAt,1,1,"renewal",CommercialExposureProjection.Locations(assessment.Proposed))]:assessment.Slices.Select((slice,index)=>new CommercialExposureSlice(book.Value,draft.PolicyId,term.Id,Guid.NewGuid(),term.StartsAt,term.EndsAt,
                slice.EffectiveAt,revision.CreatedAt,sequence,index+1,"adjustment",CommercialExposureProjection.Locations(slice.Proposed))).ToArray();
            var existing=await CommercialExposureProjection.ReadAsync(db,book.Value,known,token);
            var limits=await CommercialExposureProjection.LimitsAsync(db,book.Value,known,token);
            var result=CommercialExposureRules.Assess(existing,proposed,limits,book.Value,draft.PolicyId,proposed[0].EffectiveAt,renewalTerm?.EndsAt??term.EndsAt,known);
            return Body(internalAudience,observed,effective,known,"proposed",book.Value,result.Intervals,source);
        }
        catch(Exception error) when(error is ArgumentException or QuoteInputException or QuoteValidationException or KeyNotFoundException or InvalidOperationException)
        {return Unavailable("commercial-exposure-proposal-incomplete");}
    }

    private static async Task<Dictionary<string, object>> Policy(BackOfficeDbContext db, Guid policyId, bool internalAudience,
        DateTimeOffset observed, DateTimeOffset effective, DateTimeOffset known, CancellationToken token)
    {
        var candidates = await PolicyTemporalSelector.Candidates(db, policyId, known).ToArrayAsync(token);
        var selection = PolicyTemporalSelector.Select(candidates, policyId, effective, known);
        if (selection is null) return Body(internalAudience, observed, effective, known, "not-covered", null, [], null);
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == selection.Candidate.VersionId && x.PolicyId == policyId, token);
        var source = new Source("policy-version", version.Id, Convert.ToHexStringLower(version.ContentHash));
        var header = await db.Set<CommercialExposureVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.VersionId == version.Id, token);
        if (header is null) return Body(internalAudience, observed, effective, known, "unavailable", null, [], source, "commercial-exposure-source-unavailable");
        using var snapshot = JsonDocument.Parse(version.SnapshotJson);
        var districts = CommercialExposureProjection.Locations(snapshot.RootElement).Select(x => x.District).Distinct(StringComparer.Ordinal).ToArray();
        var existing = await CommercialExposureProjection.ReadAsync(db, header.BookId, known, token);
        var limits = await CommercialExposureProjection.LimitsAsync(db, header.BookId, known, token);
        var assessment = CommercialExposureRules.Observe(existing, limits, header.BookId, policyId, districts, effective, known);
        return Body(internalAudience, observed, effective, known, selection.State, header.BookId, assessment.Intervals, source);
    }

    private static async Task<Dictionary<string, object>> Quote(BackOfficeDbContext db, Quote quote, bool internalAudience,
        DateTimeOffset observed, DateTimeOffset effective, DateTimeOffset known, CancellationToken token)
    {
        var revision = await db.Set<QuoteRevision>().AsNoTracking().Where(x => x.QuoteId == quote.Id && x.SavedAt <= known)
            .OrderByDescending(x => x.Number).FirstOrDefaultAsync(token);
        if (revision is null || revision.AgencyId != quote.AgencyId || revision.ClientId != quote.ClientId || revision.RelationshipId != quote.RelationshipId)
            return Body(internalAudience, observed, effective, known, "not-covered", null, [], null);
        var source = new Source("quote-revision", revision.Id, Convert.ToHexStringLower(revision.ContentHash));
        using var proposal = JsonDocument.Parse(revision.ProposalJson); using var intent = JsonDocument.Parse(revision.TermIntentJson);
        var term = QuoteTerm.Assess(intent.RootElement).Term;
        CommercialExposureLocation[] locations;
        try { locations = CommercialExposureProjection.Locations(proposal.RootElement); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        { return Body(internalAudience, observed, effective, known, "unavailable", null, [], source, "commercial-exposure-proposal-incomplete"); }
        if (term is null || locations.Length == 0)
            return Body(internalAudience, observed, effective, known, "unavailable", null, [], source, "commercial-exposure-proposal-incomplete");
        var book = await (from version in db.Set<ProductVersion>().AsNoTracking() join row in db.Set<CommercialExposureBook>().AsNoTracking()
            on new { version.ProductId, version.ProviderId } equals new { row.ProductId, row.ProviderId }
            where version.Id == revision.ProductVersionId && version.ProductId == quote.ProductId select (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (book is null) return Body(internalAudience, observed, effective, known, "unavailable", null, [], source, "commercial-exposure-book-unavailable");
        // The quote is not issued. The temporary policy identity exists only in
        // this pure preview and is never persisted or disclosed in its response.
        var policyId = Guid.NewGuid();
        var proposed = new CommercialExposureSlice(book.Value, policyId, Guid.NewGuid(), revision.Id, term.StartsAt, term.EndsAt,
            term.StartsAt, revision.SavedAt, 1, 1, "new-business", locations);
        var existing = await CommercialExposureProjection.ReadAsync(db, book.Value, known, token);
        var limits = await CommercialExposureProjection.LimitsAsync(db, book.Value, known, token);
        var assessment = CommercialExposureRules.Assess(existing, [proposed], limits, book.Value, policyId, term.StartsAt, term.EndsAt, known);
        return Body(internalAudience, observed, effective, known, "proposed", book.Value, assessment.Intervals, source);
    }

    private static Dictionary<string, object> Body(bool internalAudience, DateTimeOffset observed, DateTimeOffset effective, DateTimeOffset known,
        string state, Guid? bookId, IReadOnlyList<CommercialExposureInterval> intervals, Source? source, string? blocker = null)
    {
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string Outcome(string? code) => code is null ? "within-capacity" : code.Contains("exceeded", StringComparison.Ordinal) ? "exceeds-capacity" : "unavailable";
        Dictionary<string, object> Own(CommercialExposureInterval row) => new() { ["district"] = row.District,
            ["interval"] = new { startsAt = row.From, endsAt = row.To }, ["ownProposedSumInsured"] = Money(row.ProposedPropertySum), ["outcome"] = Outcome(row.Blocker) };
        Dictionary<string, object> Internal(CommercialExposureInterval row)
        {
            var item = Own(row); item["bookSumInsured"] = Money(row.ResultingPropertySum); item["policyCount"] = row.PolicyCount; item["bookId"] = bookId!.Value;
            if (row.Blocker is not null) item["blocker"] = row.Blocker;
            if (row.Limit is decimal limit) { item["limit"] = Money(limit); item["headroom"] = Money(row.Headroom!.Value); item["limitVersionId"] = row.LimitVersionId!.Value; item["limitHash"] = row.LimitHash!; }
            return item;
        }
        var outcome = blocker is not null || intervals.Count == 0 || intervals.Any(x => Outcome(x.Blocker) == "unavailable") ? "unavailable"
            : intervals.Any(x => x.Blocker is not null) ? "exceeds-capacity" : "within-capacity";
        var result = new Dictionary<string, object> { ["format"] = "commercial-exposure-1", ["audience"] = internalAudience ? "internal" : "agency",
            ["observedAt"] = observed, ["effectiveAt"] = effective, ["knownAt"] = known, ["advisory"] = true, ["coverageState"] = state, ["outcome"] = outcome,
            ["truncated"] = intervals.Count > MaximumRows, ["districts"] = intervals.Take(MaximumRows).Select(row => internalAudience ? Internal(row) : Own(row)).ToArray() };
        if (source is not null) result["source"] = new { kind = source.Kind, id = source.Id, hash = source.Hash };
        if (blocker is not null) result["blocker"] = blocker;
        return result;
    }

    private static async Task<OwnedQuoteScope> HoldQuote(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    {
        if (actor.AgencyId is not Guid agencyId) return await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        try { await AgencyScope.Resolve(db, actor, agencyId, "agency-sharing-read", token); }
        catch (AgencyCommandException) { throw Denied(); }
        var quote = await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(HOLDLOCK) WHERE Id={quoteId} AND AgencyId={agencyId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (quote is null || quote.CurrentRevisionId is null) throw Missing();
        var client = await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM ClientAccount WITH(HOLDLOCK) WHERE Id={quote.ClientId}").AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        var relationship = await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK) WHERE Id={quote.RelationshipId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (relationship is null || relationship.AgencyId != agencyId || relationship.ClientId != quote.ClientId || relationship.State != "active") throw Missing();
        var agency = await db.Set<Agency>().AsNoTracking().SingleAsync(x => x.Id == agencyId, token);
        return new(new(actor, agency, client, relationship), quote);
    }
    private static QuoteOperationException Missing() => new(404, "commercial-exposure-subject-not-found");
    private static QuoteOperationException Denied() => new(403, "commercial-exposure-access-denied");
}
