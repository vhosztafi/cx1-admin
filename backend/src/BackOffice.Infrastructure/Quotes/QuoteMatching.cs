using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

// Matching concerns the account identity selected for the intake. Proposal
// declarations stay independent and are never used to overwrite that identity.
public static class QuoteMatching
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<(string? Code, Guid? ReviewId)> AssessAsync(BackOfficeDbContext db, Quote quote, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Matching assessment requires held quote scope.");
        var intake = await db.Set<MatchSubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.QuoteId == quote.Id, token);
        if (intake is null) return (null, null);
        var review = await db.Set<MatchReview>().AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == intake.Id, token);
        if (intake.LinkedClientId != quote.ClientId || intake.LinkedRelationshipId != quote.RelationshipId || intake.AgencyId != quote.AgencyId)
            return ("quote-match-context-changed", review?.Id);
        return (review?.State is "linked" or "separate" ? null : "quote-match-review-required", review?.Id);
    }

    public static async Task<MatchSubmission?> HoldIntakeAsync(BackOfficeDbContext db, ActorContext actor,
        Guid relationshipId, Guid? submissionId, CancellationToken token)
    {
        if (submissionId is null) return null;
        var hint = await db.Set<ClientAgencyRelationship>().AsNoTracking().Where(x => x.Id == relationshipId)
            .Select(x => new { x.AgencyId, x.ClientId }).SingleOrDefaultAsync(token) ?? throw new QuoteOperationException(404, "quote-context-not-found");
        await QuoteScope.AuthorizeAgencyAsync(db, actor, hint.AgencyId, QuoteAccess.Capture, token);
        var intake = await db.Set<MatchSubmission>().FromSqlInterpolated($"SELECT * FROM MatchSubmission WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={submissionId}").SingleOrDefaultAsync(token);
        if (intake is null || intake.AgencyId != hint.AgencyId || intake.LinkedClientId != hint.ClientId || intake.LinkedRelationshipId != relationshipId)
            throw new QuoteOperationException(404, "quote-match-context-not-found");
        // Keep review decisions serialized with the attachment and receipt.
        if (await db.Set<MatchReview>().FromSqlInterpolated($"SELECT * FROM MatchReview WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE SubmissionId={intake.Id}").SingleOrDefaultAsync(token) is null)
            throw new QuoteOperationException(409, "quote-match-review-required");
        return intake;
    }

    public static async Task AttachOrReviewAsync(BackOfficeDbContext db, Quote quote, QuoteRelationshipScope scope,
        MatchSubmission? intake, Guid actor, DateTimeOffset now, CancellationToken token)
    {
        if (intake is not null)
        {
            if (intake.QuoteId is not null) throw new QuoteOperationException(409, "quote-match-already-attached");
            if (intake.AgencyId != quote.AgencyId || intake.LinkedClientId != quote.ClientId || intake.LinkedRelationshipId != quote.RelationshipId)
                throw new QuoteOperationException(409, "quote-match-context-changed");
            intake.QuoteId = quote.Id; await db.SaveChangesAsync(token); return;
        }
        var client = scope.Client;
        var candidate = await (from other in db.Set<ClientAccount>().AsNoTracking()
            join relationship in db.Set<ClientAgencyRelationship>().AsNoTracking() on other.Id equals relationship.ClientId
            where other.Id != client.Id && other.IdentityState == "active" && relationship.State == "active" &&
                (other.NormalizedName == client.NormalizedName || client.CompanyNumber != null && other.CompanyNumber == client.CompanyNumber)
            orderby (client.CompanyNumber != null && other.CompanyNumber == client.CompanyNumber) descending, other.Reference, relationship.Id
            select new { Client = other, Relationship = relationship }).FirstOrDefaultAsync(token);
        if (candidate is null) return;
        ClientWrite identity;
        try
        {
            var valid = ClientIdentity.Validate(new(client.LegalName, client.EntityType,
                JsonSerializer.Deserialize<AddressWrite>(client.Address, Json), client.CompanyNumber));
            identity = new(valid.LegalName, valid.EntityType, valid.Address, valid.CompanyNumber);
        }
        catch (Exception error) when (error is PartyValidationException or JsonException)
        {
            // Legacy incomplete accounts can retain drafts but cannot gain readiness.
            return;
        }
        var setting = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "matching-rule" && x.EffectiveFrom <= now)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token) ?? throw new QuoteOperationException(503, "quote-matching-configuration-unavailable");
        MatchRuleSnapshot rule;
        try { rule = JsonSerializer.Deserialize<MatchRuleSnapshot>(setting.Values, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new QuoteOperationException(503, "quote-matching-configuration-unavailable"); }
        if (rule.Id != setting.Id || rule.Version != setting.Version) throw new QuoteOperationException(503, "quote-matching-configuration-unavailable");
        var signals = new List<MatchSignal> { new("legal-name", "Comparison of recorded business names", client.LegalName, candidate.Client.LegalName,
            "strong", client.NormalizedName == candidate.Client.NormalizedName ? "match" : "different") };
        if (client.CompanyNumber is { Length: > 0 } company && candidate.Client.CompanyNumber is { Length: > 0 } candidateCompany)
            signals.Add(new("company-number", "Comparison of recorded company numbers", company, candidateCompany, "definitive", company == candidateCompany ? "match" : "different"));
        ValidatedMatchEvidence evidence;
        try { evidence = MatchRules.ValidateEvidence(identity, signals, rule, "high"); }
        catch (PartyValidationException) { throw new QuoteOperationException(503, "quote-matching-configuration-unavailable"); }
        if (!rule.RequireReview && rule.DuplicateQuotePolicy == "allow-competing") return;
        intake = new MatchSubmission { Reference = "MI-" + quote.Number.ToString("D10"), AgencyId = quote.AgencyId, QuoteId = quote.Id,
            LinkedClientId = quote.ClientId, LinkedRelationshipId = quote.RelationshipId,
            IdentitySnapshot = JsonSerializer.Serialize(evidence.Identity, Json), CreatedBy = actor, CreatedAt = now, UpdatedAt = now };
        var review = new MatchReview { SubmissionId = intake.Id, CandidateClientId = candidate.Client.Id, CandidateRelationshipId = candidate.Relationship.Id,
            RuleVersionId = setting.Id, RuleSnapshot = JsonSerializer.Serialize(rule, Json), Signals = JsonSerializer.Serialize(evidence.Signals, Json),
            Confidence = evidence.Confidence, CreatedBy = actor, CreatedAt = now, UpdatedAt = now };
        db.AddRange(intake, review); await db.SaveChangesAsync(token);
        db.Add(new ClientActivity { ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, RecordId = review.Id, RecordKind = "match",
            EventType = "match.created", ActorId = actor, CreatedBy = actor, CreatedAt = now, OccurredAt = now });
        await db.SaveChangesAsync(token);
    }
}
