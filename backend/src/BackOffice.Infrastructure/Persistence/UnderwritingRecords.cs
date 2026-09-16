namespace BackOffice.Infrastructure.Persistence;

public sealed class RatingRuleVersion : MutableRecord
{
    public Guid ProductId { get; set; }
    public string Version { get; set; } = "";
    public string State { get; set; } = "published";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
    public string DefinitionJson { get; set; } = "{}";
}
public sealed class BinderVersion : MutableRecord
{
    public Guid ProductId { get; set; }
    public Guid ProviderId { get; set; }
    public string Version { get; set; } = "";
    public string State { get; set; } = "published";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
    public string DefinitionJson { get; set; } = "{}";
}
public sealed class AuthorityVersion : MutableRecord
{
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public string Version { get; set; } = "";
    public string State { get; set; } = "published";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
    public string DefinitionJson { get; set; } = "{}";
}
public sealed class UserAuthorityGrant : MutableRecord
{
    public Guid UserId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
    public Guid GrantedBy { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
}
public sealed class UnderwritingCycle : MutableRecord
{
    public Guid QuoteId { get; set; }
    public Guid QuoteRevisionId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid ClientId { get; set; }
    public Guid RelationshipId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public Guid RatingRuleVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public int Sequence { get; set; }
    public Guid WorkId { get; set; }
    public byte[] PricingInputHash { get; set; } = [];
    public string InputJson { get; set; } = "{}";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public Guid RequestedBy { get; set; }
    public string State { get; set; } = "rating-pending";
    public Guid? CurrentRatingId { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public string? SupersededReason { get; set; }
}
public sealed class QuoteRatingResult : StoredRecord
{
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid WorkId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid RuleVersionId { get; set; }
    public byte[] InputHash { get; set; } = [];
    public string ResultJson { get; set; } = "{}";
    public string Outcome { get; set; } = "rated";
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public decimal AnnualPremium { get; set; }
    public decimal TermPremium { get; set; }
    public decimal Tax { get; set; }
    public decimal Fee { get; set; }
    public decimal GrossPayable { get; set; }
    public decimal BrokerCommission { get; set; }
}
public sealed class QuoteSubmission : StoredRecord
{
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public int Sequence { get; set; }
    public string OperationKey { get; set; } = "";
    public Guid SubmittedBy { get; set; }
    public Guid RoutingVersionId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedTeamId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class QuoteReferral : MutableRecord
{
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid RatingId { get; set; }
    public int Sequence { get; set; }
    public string RuleCode { get; set; } = "";
    public string Dimension { get; set; } = "";
    public Guid? RiskItemId { get; set; }
    public Guid TargetKey { get; set; }
    public string RequiredAuthorityJson { get; set; } = "{}";
    public string Reason { get; set; } = "";
    public string State { get; set; } = "open";
    public Guid? AssignedUserId { get; set; }
}
