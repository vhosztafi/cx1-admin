using BackOffice.Application.Quotes;

namespace BackOffice.Application.Underwriting;

public sealed record UnderwritingRefreshContext(Guid QuoteProductId, Guid TargetProductId, Guid CurrentRevisionId,
    Guid ExpectedRevisionId, Guid CurrentProductVersionId, Guid TargetProductVersionId, string QuoteState,
    bool CaptureClosed, string TargetState, DateTimeOffset TargetEffectiveFrom, DateTimeOffset? TargetEffectiveTo,
    DateTimeOffset Now, ResolvedQuoteTerm Term);

public static class UnderwritingLifecycleRules
{
    // A successful gate authorises creating a NEW revision only. The service
    // must separately revalidate typed proposal, current agency distribution/
    // approved terms and capture pins under its held scope before any write.
    public static string? RefreshBlocker(UnderwritingRefreshContext context)
    {
        if (new[] { context.QuoteProductId, context.TargetProductId, context.CurrentRevisionId, context.ExpectedRevisionId,
                context.CurrentProductVersionId, context.TargetProductVersionId }.Any(x => x == Guid.Empty)) return "refresh-context-required";
        if (context.QuoteState != "draft" || context.CaptureClosed) return "quote-capture-closed";
        if (context.CurrentRevisionId != context.ExpectedRevisionId) return "quote-revision-stale";
        if (context.QuoteProductId != context.TargetProductId) return "refresh-product-mismatch";
        if (context.TargetState != "published" || context.TargetEffectiveFrom > context.Now || context.TargetEffectiveTo <= context.Now ||
            context.TargetEffectiveFrom > context.Term.StartsAt || context.TargetEffectiveTo < context.Term.EndsAt) return "refresh-product-unavailable";
        try { _ = QuoteRatingRules.CivilDuration(context.Term); }
        catch (ArgumentException) { return "refresh-term-invalid"; }
        return null;
    }

    public static bool CanReturnToDraft(string state) => state is "rating-pending" or "rated" or "referred" or "approved" or "sent" or "accepted" or "declined";

    public static bool ProofBlocks(string requirement, string stage)
    {
        if (stage is not ("rate" or "prepare-terms" or "send" or "accept" or "issue")) throw new ArgumentException("Unsupported progression stage.");
        if (requirement is not ("driver-proof" or "premises-security" or "signed-statement" or "trading-history" or "condition-proof"))
            throw new ArgumentException("Unsupported evidence requirement.");
        if (stage == "rate") return false;
        return stage != "prepare-terms" || requirement != "signed-statement";
    }
}
