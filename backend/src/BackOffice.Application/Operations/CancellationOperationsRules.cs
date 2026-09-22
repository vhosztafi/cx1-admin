namespace BackOffice.Application.Operations;

public static class CancellationOperationsRules
{
    public static bool CanApply(string kind, DateTimeOffset effectiveAt, DateTimeOffset now)
        => kind == "notice" || kind is "certificate-withdrawal" or "task-close" && now >= effectiveAt;
    public static bool CanCloseRenewal(Guid cancelledTermId, Guid? taskTermId, string? sourceKind,
        string typeCode, string state, bool sourceChanged)
        => cancelledTermId != Guid.Empty && taskTermId == cancelledTermId && sourceKind == "policy-term"
            && typeCode == "renewal" && !sourceChanged && state is "open" or "in-progress" or "awaiting-information" or "blocked";
    public static bool NeedsNoticeDelivery(bool hasLegacyReceipt) => !hasLegacyReceipt;
}
