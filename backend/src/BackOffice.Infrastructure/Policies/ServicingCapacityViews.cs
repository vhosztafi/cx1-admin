using System.Text.Json;
using BackOffice.Application.Policies;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingCapacitySummary(Guid Id,Guid DraftId,Guid CycleId,Guid RevisionId,Guid RatingId,Guid ReferralId,Guid ProviderId,
    string ProviderLabel,string RuleCode,string Dimension,Guid? RiskItemId,string State,string Etag,string Reason,DateTimeOffset RaisedAt,
    Guid? CurrentSubmissionId,Guid? CurrentResponseId,Guid? AssignedUserId);
public sealed record ServicingCapacityList(Guid DraftId,Guid PolicyId,string DraftEtag,IReadOnlyList<ServicingCapacitySummary> Items,Guid? NextBeforeId);
public sealed record ServicingCapacityOption(Guid Id,string Label);
public sealed record ServicingCapacitySubmissionView(Guid Id,int Sequence,string Body,string Reason,string ContextHash,Guid WorkId,string JobState,
    int Attempts,string? ErrorCode,Guid ScenarioVersionId,DateTimeOffset SubmittedAt,DateTimeOffset ResponseDueAt,IReadOnlyList<Guid> EvidenceIds);
public sealed record ServicingCapacityMessageView(Guid Id,int Sequence,Guid SubmissionId,string Kind,string Body,Guid RecordedBy,string RecordedByLabel,DateTimeOffset RecordedAt);
public sealed record ServicingCapacityResponseView(Guid Id,int Sequence,Guid SubmissionId,string Provenance,string Outcome,string Body,
    string ProviderUnderwriter,string ProviderReference,string ApplicationState,Guid? EvidenceAssociationId,Guid? EvidenceReviewId,
    DateTimeOffset ReceivedAt,DateTimeOffset RecordedAt,ServicingCapacityResponseDefinition Definition);
public sealed record ServicingCarrierConditionView(Guid Id,int Sequence,string Code,string Kind,string Etag,string Wording,JsonElement Definition,
    IReadOnlyList<DateTimeOffset> EffectiveDates,bool Satisfied);
public sealed record ServicingCarrierResolutionView(Guid Id,int Sequence,Guid AssociationId,Guid ReviewId,string Outcome,string Reason,
    Guid ActorId,Guid AuthorityVersionId,Guid GrantId,DateTimeOffset RecordedAt);
public sealed record ServicingCapacityHistoryPage<T>(IReadOnlyList<T> Items,int? NextAfterSequence);
public sealed record ServicingCapacityDetail(ServicingCapacitySummary Case,Guid PolicyId,string DraftEtag,bool Current,bool CanWrite,bool Ready,
    IReadOnlyList<string> Blockers,ServicingCapacitySubmissionView? Submission,ServicingCapacityResponseView? Response,
    IReadOnlyList<ServicingCarrierConditionView> Conditions,IReadOnlyList<ServicingCapacityOption> Scenarios,
    IReadOnlyList<ServicingCapacityOption> Seniors,IReadOnlyList<ServicingCapacitySummary> SimilarCases);
