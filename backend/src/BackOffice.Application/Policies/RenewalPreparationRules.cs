using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record RenewalPreparedTerm(ResolvedQuoteTerm Term,JsonElement Intent);
public sealed record RenewalExperienceFacts(DateOnly ObservationStartsOn,DateOnly ObservationEndsOn,int ClaimCount,
    decimal Paid,decimal Outstanding,decimal EarnedPremium,string SourceCode,string SourceReference,Guid EvidenceAssociationId);
public sealed record RenewalExperienceAssessment(bool InformationComplete,string State,decimal? LossRatio,bool RequiresSeniorDecision,int LoadingBasisPoints);

public static class RenewalPreparationRules
{
    private static readonly TimeZoneInfo London=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static RenewalPreparedTerm Term(DateTimeOffset expiringEnd,int months,IReadOnlyCollection<int> allowedMonths,int? endOffsetMinutes=null)
    {
        ArgumentNullException.ThrowIfNull(allowedMonths);
        if(expiringEnd.Offset!=TimeSpan.Zero || expiringEnd.Ticks%TimeSpan.TicksPerMinute!=0 || months is <1 or >12 ||
            !allowedMonths.Contains(months) || endOffsetMinutes is not(null or 0 or 60))
            throw new ArgumentException("Renewal requires the exact UTC term end and an enabled term length.");
        var start=TimeZoneInfo.ConvertTime(expiringEnd,London);DateTime end;
        try{end=start.DateTime.AddMonths(months);}catch(ArgumentOutOfRangeException){throw new ArgumentException("Renewal anniversary is outside the supported calendar.");}
        var intent=new Dictionary<string,object>{["kind"]=months==12?"annual":"short-period",["timeZone"]="Europe/London",
            ["localStartDate"]=start.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["localStartTime"]=start.ToString("HH:mm",CultureInfo.InvariantCulture),
            ["utcOffsetMinutes"]=(int)start.Offset.TotalMinutes};
        if(months!=12){intent["localEndDate"]=end.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);intent["localEndTime"]=end.ToString("HH:mm",CultureInfo.InvariantCulture);}
        if(endOffsetMinutes.HasValue)intent["endUtcOffsetMinutes"]=endOffsetMinutes.Value;
        var encoded=JsonSerializer.SerializeToElement(intent);var result=QuoteTerm.Assess(encoded);
        if(result.Term is null || result.Term.StartsAt!=expiringEnd)throw new ArgumentException("Renewal term has an invalid or ambiguous London anniversary: "+string.Join(",",result.Issues.Select(x=>x.Code)));
        return new(result.Term,encoded);
    }

    public static RenewalExperienceAssessment Experience(RenewalExperienceFacts? facts,bool evidenceAccepted,DateTimeOffset now,
        int referralThresholdBasisPoints,int loadingBasisPoints)
    {
        if(now.Offset!=TimeSpan.Zero || referralThresholdBasisPoints is <0 or >100000 || loadingBasisPoints is <0 or >10000)
            throw new ArgumentException("Experience assessment requires the configured UTC clock and bounded pinned rules.");
        if(facts is null)return new(false,"missing-experience",null,false,0);
        ValidateExperience(facts,now);
        if(!evidenceAccepted)return new(false,"experience-evidence-required",null,false,0);
        if(facts.EarnedPremium==0)return new(false,"zero-earned-premium",null,false,0);
        var losses=facts.Paid+facts.Outstanding;
        var refer=losses*10000m>facts.EarnedPremium*referralThresholdBasisPoints;
        return new(true,refer?"UW-31":"clear",losses/facts.EarnedPremium,refer,refer?loadingBasisPoints:0);
    }

    public static void ValidateExperience(RenewalExperienceFacts facts,DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if(now.Offset!=TimeSpan.Zero)throw new ArgumentException("Experience observation requires the UTC processing clock.");
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,London).DateTime);
        if(facts.ObservationStartsOn>=facts.ObservationEndsOn || facts.ObservationEndsOn>today || facts.ClaimCount is <0 or >100000 ||
            !Money(facts.Paid) || !Money(facts.Outstanding) || !Money(facts.EarnedPremium) ||
            facts.SourceCode is not("insured" or "agency" or "administrator") || string.IsNullOrWhiteSpace(facts.SourceReference) ||
            facts.SourceReference.Length>200 || facts.EvidenceAssociationId==Guid.Empty)
            throw new ArgumentException("Experience requires an ordered observed period, exact nonnegative amounts, source and owned evidence.");
    }

    private static bool Money(decimal value)=>value is >=0 and <=QuoteRatingRules.MaximumMoney && decimal.Round(value,2)==value;
}
