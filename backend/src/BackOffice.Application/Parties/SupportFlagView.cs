namespace BackOffice.Application.Parties;

// Restricted detail/history DTO. Never put this value in a generic command receipt.
public sealed record SupportFlagView(Guid Id,Guid PersonId,Guid OriginRelationshipId,string TypeCode,string InternalCategory,
    string InternalInstruction,string ConsentBasis,DateOnly ReviewOn,string Reason,Guid[] VisibleRelationshipIds,
    string? AgencyInstruction=null,DateTimeOffset? EndedAt=null);
