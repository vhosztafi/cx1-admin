using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingTermsSubject(Guid DraftId,Guid CycleId,Guid RevisionId,Guid BaseVersionId,Guid RatingId,Guid TermsId,string TermsHash);

public static class ServicingTermsRules
{
    // Pure applicability only. The owning service must also hold current scope,
    // grants, template/configuration, recipient and purpose-specific proof.
    public static bool CanApplyDelivery(ServicingTermsSubject incoming,ServicingTermsSubject current,Guid deliveryId,Guid? currentDeliveryId,
        string state,DateTimeOffset now,DateTimeOffset expiresAt)=>
        SameTerms(incoming,current) && deliveryId!=Guid.Empty && deliveryId==currentDeliveryId && state=="queued" &&
        Utc(now) && Utc(expiresAt) && now<expiresAt;

    public static bool CanAccept(ServicingTermsSubject supplied,ServicingTermsSubject current,Guid deliveryId,Guid? currentDeliveryId,
        string deliveryState,DateTimeOffset? deliveredAt,DateTimeOffset acceptedAt,DateTimeOffset now,DateTimeOffset expiresAt,
        string suppliedAssurance,string currentAssurance,string accepter,string channel)=>
        SameTerms(supplied,current) && deliveryId!=Guid.Empty && deliveryId==currentDeliveryId &&
        ReferralRules.Hash(suppliedAssurance) && suppliedAssurance==currentAssurance &&
        deliveredAt is {} delivered && Utc(delivered) && Utc(acceptedAt) && Utc(now) && Utc(expiresAt) &&
        QuoteTermsRules.AcceptanceWindow(deliveryState,deliveredAt,acceptedAt,now,expiresAt) &&
        QuoteTermsRules.ValidAcceptanceIdentity(accepter,channel);

    private static bool SameTerms(ServicingTermsSubject left,ServicingTermsSubject right)=>left is not null && right is not null &&
        left==right && new[]{right.DraftId,right.CycleId,right.RevisionId,right.BaseVersionId,right.RatingId,right.TermsId}.All(x=>x!=Guid.Empty) &&
        ReferralRules.Hash(right.TermsHash);
    private static bool Utc(DateTimeOffset value)=>value.Offset==TimeSpan.Zero;
}
