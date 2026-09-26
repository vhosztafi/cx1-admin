using System.Data;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Reporting;

public sealed record SearchFilters(string Q = "", string Kind = "all", string? Status = null, string? ProductCode = null,
    Guid? AgencyId = null, Guid? ProviderId = null, Guid? UnderwriterId = null, DateOnly? From = null,
    DateOnly? To = null, string? Reference = null, int Offset = 0, string? Version = null, DateTimeOffset? AsOf = null);
public sealed record SearchHit { public Guid Id { get; init; } public string Kind { get; init; } = ""; public string Reference { get; init; } = ""; public string Label { get; init; } = ""; public string Status { get; init; } = ""; public string? ProductCode { get; init; } public string Href { get; init; } = ""; }
public sealed record SearchResult(SearchFilters Filters, DateTimeOffset AsOf, string Version, int Total, int? NextOffset, string[] AvailableKinds, SearchHit[] Items);

public sealed class SearchService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<object> OptionsAsync(ActorContext actor, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        actor = await ReportingScope.Current(db, actor, token);
        var risk = actor.HasCapability("quote-read");
        var products = await db.Set<Product>().Where(x => risk).OrderBy(x => x.Name).Select(x => new { x.Code, x.Name }).ToArrayAsync(token);
        var agencies = await db.Set<Agency>().Where(x => risk || actor.HasCapability("agency-read") || actor.HasCapability("client-read")).OrderBy(x => x.LegalName).Select(x => new { x.Id, Name = x.LegalName }).ToArrayAsync(token);
        var providers = await db.Set<CapacityProvider>().Where(x => risk).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToArrayAsync(token);
        var users = await db.Set<StaffUser>().Where(x => risk && db.Set<Quote>().Any(q => q.AssignedUserId == x.Id)).OrderBy(x => x.DisplayName).Select(x => new { x.Id, Name = x.DisplayName }).ToArrayAsync(token);
        await tx.CommitAsync(token); return new { products, agencies, providers, underwriters = users, kinds = Kinds.Where(x => actor.HasCapability(Capability(x))) };
    }
    public static readonly string[] Kinds = ["client", "quote", "policy", "agency", "match"];
    public static string Capability(string kind) => kind switch { "client" => "client-read", "quote" => "quote-read", "policy" => "policy-read", "agency" => "agency-read", "match" => "match-read", _ => "invalid" };
    public async Task<SearchResult> SearchAsync(ActorContext actor, SearchFilters input, CancellationToken token = default)
    {
        Validate(input);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        actor = await ReportingScope.Current(db, actor, token);
        var available = Kinds.Where(x => actor.HasCapability(Capability(x))).ToArray();
        if (input.Kind != "all") ReportingScope.Require(actor, Capability(input.Kind));
        var version = await QuoteDiscovery.ListVersionAsync(db, token);
        if (input.Version is not null && input.Version != version) throw new QuoteOperationException(409, "search-results-changed");
        var asOf = input.AsOf ?? time.GetUtcNow();
        if (asOf > time.GetUtcNow()) throw new QuoteOperationException(400, "search-time-invalid");
        var selected = available.Where(x => input.Kind == "all" || input.Kind == x);
        var riskFilter = input.ProductCode != null || input.ProviderId != null || input.UnderwriterId != null || input.From != null || input.To != null;
        if (riskFilter) selected = selected.Where(x => x is "quote" or "policy");
        var text = input.Q.Trim().ToUpperInvariant(); var reference = input.Reference?.Trim().ToUpperInvariant();
        var result = new List<SearchHit>(); var total = 0; var skip = input.Offset;
        foreach (var kind in selected)
        {
            IQueryable<SearchHit> rows;
            if (kind == "quote")
            {
                var quotes = QuoteDiscovery.Search(db, QuoteDiscovery.Rows(db), text);
                if (input.AgencyId != null) quotes = quotes.Where(x => x.AgencyId == input.AgencyId);
                if (input.ProductCode != null) quotes = quotes.Where(x => x.ProductCode == input.ProductCode);
                if (input.From != null) quotes = quotes.Where(x => x.StartDate >= input.From);
                if (input.To != null) quotes = quotes.Where(x => x.StartDate <= input.To);
                if (input.UnderwriterId != null) quotes = quotes.Where(x => db.Set<Quote>().Any(q => q.Id == x.Id && q.AssignedUserId == input.UnderwriterId));
                if (input.ProviderId != null) quotes = quotes.Where(x => db.Set<QuoteRevision>().Any(r => r.Id == x.RevisionId && db.Set<ProductVersion>().Any(v => v.Id == r.ProductVersionId && v.ProviderId == input.ProviderId)));
                rows = quotes.Select(x => new SearchHit { Id = x.Id, Kind = "quote", Reference = x.Reference, Label = x.ClientName, Status = x.State, ProductCode = x.ProductCode });
            }
            else if (kind == "policy")
            {
                var policies = PolicyDiscoveryService.Search(db, PolicyDiscoveryService.Rows(db, asOf), text)
                    .Where(x => db.Set<Policy>().Any(p => p.Id == x.Id && db.Set<Quote>().Any(q => q.Id == p.SourceQuoteId && q.BoundPolicyId == p.Id && q.ClientId == p.ClientId && q.AgencyId == p.AgencyId && q.RelationshipId == p.RelationshipId)));
                if (input.AgencyId != null) policies = policies.Where(x => x.AgencyId == input.AgencyId);
                if (input.ProductCode != null) policies = policies.Where(x => x.ProductCode == input.ProductCode);
                if (input.From != null) policies = policies.Where(x => x.InceptionDate >= input.From);
                if (input.To != null) policies = policies.Where(x => x.InceptionDate <= input.To);
                if (input.UnderwriterId != null) policies = policies.Where(x => db.Set<Policy>().Any(p => p.Id == x.Id && db.Set<Quote>().Any(q => q.Id == p.SourceQuoteId && q.AssignedUserId == input.UnderwriterId)));
                if (input.ProviderId != null) policies = policies.Where(x => db.Set<PolicyTerm>().Any(t => t.Id == x.CurrentTermId && db.Set<ProductVersion>().Any(v => v.Id == t.ProductVersionId && v.ProviderId == input.ProviderId)));
                rows = policies.Select(x => new SearchHit { Id = x.Id, Kind = "policy", Reference = x.Reference, Label = x.ClientName, Status = x.State, ProductCode = x.ProductCode });
            }
            else if (kind == "client")
                rows = db.Set<ClientAccount>().Where(x => (text == "" || x.LegalName.ToUpper().Contains(text) || x.Reference.Contains(text)) &&
                    (input.AgencyId == null || db.Set<ClientAgencyRelationship>().Any(r => r.ClientId == x.Id && r.AgencyId == input.AgencyId)))
                    .Select(x => new SearchHit { Id = x.Id, Kind = "client", Reference = x.Reference, Label = x.LegalName, Status = x.IdentityState, ProductCode = null });
            else if (kind == "agency")
                rows = db.Set<Agency>().Where(x => (text == "" || x.LegalName.ToUpper().Contains(text) || x.Reference.Contains(text)) && (input.AgencyId == null || x.Id == input.AgencyId))
                    .Select(x => new SearchHit { Id = x.Id, Kind = "agency", Reference = x.Reference, Label = x.LegalName, Status = x.State, ProductCode = null });
            else
                rows = from m in db.Set<MatchReview>() join s in db.Set<MatchSubmission>() on m.SubmissionId equals s.Id
                    join c in db.Set<ClientAccount>() on m.CandidateClientId equals c.Id
                    join r in db.Set<ClientAgencyRelationship>() on m.CandidateRelationshipId equals r.Id
                    where r.ClientId == c.Id && (text == "" || s.Reference.Contains(text) || c.LegalName.ToUpper().Contains(text)) && (input.AgencyId == null || s.AgencyId == input.AgencyId)
                    select new SearchHit { Id = m.Id, Kind = "match", Reference = s.Reference, Label = c.LegalName, Status = m.State, ProductCode = null };
            if (input.Status != null) rows = rows.Where(x => x.Status == input.Status);
            if (reference != null) rows = rows.Where(x => x.Reference.Contains(reference));
            var count = await rows.CountAsync(token); total += count;
            if (skip >= count) { skip -= count; continue; }
            if (result.Count < 25)
            {
                var page = await rows.OrderBy(x => x.Reference).ThenBy(x => x.Id).Skip(skip).Take(25 - result.Count).ToArrayAsync(token);
                result.AddRange(page.Select(x => x with { Href = Href(x.Kind, x.Id) }));
            }
            skip = 0;
        }
        await tx.CommitAsync(token);
        return new(input, asOf, version, total, input.Offset + result.Count < total ? input.Offset + result.Count : null, available, result.ToArray());
    }
    public static string Href(string kind, Guid id) => $"/{(kind == "agency" ? "agents" : kind == "match" ? "matches" : kind + "s")}/{id}";
    public static void Validate(SearchFilters f)
    {
        if (f.Kind != "all" && !Kinds.Contains(f.Kind) || f.Q.Length > 200 || f.Reference?.Length > 100 || f.Status?.Length > 40 || f.ProductCode?.Length > 60 || f.Offset is < 0 or > 10000 || f.From > f.To || f.Offset > 0 && f.Version == null)
            throw new QuoteOperationException(400, "search-filter-invalid");
        if (f.Kind is not ("all" or "quote" or "policy") && (f.ProductCode != null || f.ProviderId != null || f.UnderwriterId != null || f.From != null || f.To != null || f.AsOf != null))
            throw new QuoteOperationException(400, "search-filter-not-applicable");
    }
}
