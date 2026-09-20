using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record CalculatedRenewalRating(CalculatedQuoteRating Price,RenewalExperienceAssessment Experience);

// Inputs are trusted projections and pinned configuration, never client prices.
// Missing experience may show a provisional risk price but cannot make the
// accompanying information assessment complete or authorize an invitation.
public static class RenewalRatingRules
{
    public static CalculatedRenewalRating Calculate(JsonElement definition,RatingFacts facts,ResolvedQuoteTerm term,int commissionBasisPoints,
        RenewalExperienceFacts? experience,bool evidenceAccepted,DateTimeOffset now,int thresholdBasisPoints,int loadingBasisPoints,
        decimal renewalFee,decimal? minimumPremium=null)
        => Complete(definition,QuoteRatingRules.Calculate(definition,facts,term,commissionBasisPoints,minimumPremium),term,
            commissionBasisPoints,experience,evidenceAccepted,now,thresholdBasisPoints,loadingBasisPoints,renewalFee);

    public static CalculatedRenewalRating CalculateCommercial(JsonElement definition,CommercialRatingFacts facts,ResolvedQuoteTerm term,int commissionBasisPoints,
        RenewalExperienceFacts? experience,bool evidenceAccepted,DateTimeOffset now,int thresholdBasisPoints,int loadingBasisPoints,
        decimal renewalFee,decimal? minimumPremium=null)
        => Complete(definition,CommercialRatingRules.Calculate(definition,facts,term,commissionBasisPoints,minimumPremium),term,
            commissionBasisPoints,experience,evidenceAccepted,now,thresholdBasisPoints,loadingBasisPoints,renewalFee);

    private static CalculatedRenewalRating Complete(JsonElement definition,CalculatedQuoteRating basis,ResolvedQuoteTerm term,int commissionBasisPoints,
        RenewalExperienceFacts? experience,bool evidenceAccepted,DateTimeOffset now,int thresholdBasisPoints,int loadingBasisPoints,decimal renewalFee)
    {
        if(renewalFee<0 || renewalFee>QuoteRatingRules.MaximumMoney || Round(renewalFee)!=renewalFee)
            throw new ArgumentException("Renewal fee must be a bounded configured amount in exact pennies.");
        var assessment=RenewalPreparationRules.Experience(experience,evidenceAccepted,now,thresholdBasisPoints,loadingBasisPoints);
        var loading=Round(basis.AnnualPremium*assessment.LoadingBasisPoints/10000m);
        var annual=basis.AnnualPremium+loading;
        if(annual>UnderwritingConfiguration.Amount(definition,"maximumAnnualPremium") || annual>QuoteRatingRules.MaximumMoney)
            throw new ArgumentException("Loaded renewal premium exceeds the configured maximum.");
        var premium=term.Kind=="annual"?annual:Round(annual*basis.CivilDays/basis.AnnualCivilDays);
        var tax=Round(premium*definition.GetProperty("taxRateBps").GetInt32()/10000m);
        var commission=Round(premium*commissionBasisPoints/10000m);
        var gross=premium+tax+renewalFee;
        if(premium<=0 || gross>QuoteRatingRules.MaximumMoney)
            throw new ArgumentException("Renewal amount is outside the supported range.");
        var factors=basis.Factors.ToList();
        if(loading>0)factors.Add(new("renewal-experience",loading,"charge",basis.AnnualPremium,assessment.LoadingBasisPoints));
        return new(new(annual,premium,tax,renewalFee,gross,commission,basis.CivilDays,basis.AnnualCivilDays,factors.AsReadOnly()),assessment);
    }

    private static decimal Round(decimal value)=>decimal.Round(value,2,MidpointRounding.AwayFromZero);
}
