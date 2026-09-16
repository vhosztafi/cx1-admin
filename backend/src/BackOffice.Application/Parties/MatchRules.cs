using System.Text.Json.Serialization;

namespace BackOffice.Application.Parties;

public sealed record MatchDecisionWrite([property:JsonRequired]string? Outcome,[property:JsonRequired]string? Reason,Guid? CandidateClientId=null,string? ExpectedQuoteEtag=null);
public sealed record ValidatedMatchDecision(string Outcome,string Reason,Guid? CandidateClientId,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ExpectedQuoteEtag=null);
public sealed record MatchSignal(string Code,string Summary,string SubmittedValue,string CandidateValue,string Weight,string Result);
public sealed record MatchRuleSnapshot(Guid Id,int Version,string DuplicateQuotePolicy,[property:JsonRequired]bool RequireReview,string Summary);
public sealed record ValidatedMatchEvidence(ClientWrite Identity,MatchSignal[] Signals,MatchRuleSnapshot Rule,string Confidence);
public sealed class MatchTransitionException() : Exception("This decision is not available in the current review state.");

public static class MatchRules
{
    public static ValidatedMatchDecision Validate(MatchDecisionWrite input)
    {
        var issues=new List<PartyFieldIssue>();
        if(input.Outcome is not ("link" or "separate" or "decline" or "query" or "reopen"))
            issues.Add(new("/outcome","invalid-choice","Choose a supported match decision."));
        var reason=Text(input.Reason,1000,"/reason",issues);
        if(input.CandidateClientId==Guid.Empty || (input.CandidateClientId is not null && input.Outcome!="link"))
            issues.Add(new("/candidateClientId","invalid-candidate","Only Link may identify the saved candidate."));
        if(input.ExpectedQuoteEtag is not null)
        {
            Span<byte> bytes=stackalloc byte[8];
            var etag=input.ExpectedQuoteEtag;
            if(etag.Length!=14 || etag[0]!='"' || etag[^1]!='"' || !Convert.TryFromBase64String(etag[1..^1],bytes,out var count) || count!=8 || Convert.ToBase64String(bytes)!=etag[1..^1])
                issues.Add(new("/expectedQuoteEtag","invalid-version","Reload the attached quote version."));
        }
        if(issues.Count>0)throw new PartyValidationException(issues);
        return new(input.Outcome!,reason,input.CandidateClientId,input.ExpectedQuoteEtag);
    }

    public static string NextState(string state,string outcome) => (state,outcome) switch
    {
        ("pending" or "queried","link")=>"linked",
        ("pending" or "queried","separate")=>"separate",
        ("pending" or "queried","decline")=>"declined",
        ("pending" or "queried","query")=>"queried",
        ("linked" or "separate" or "declined","reopen")=>"pending",
        _=>throw new MatchTransitionException()
    };

    public static ValidatedMatchEvidence ValidateEvidence(ClientWrite identity,IReadOnlyList<MatchSignal>? signals,MatchRuleSnapshot rule,string confidence)
    {
        var client=ClientIdentity.Validate(identity);var issues=new List<PartyFieldIssue>();
        if(confidence is not ("low" or "medium" or "high"))issues.Add(new("/confidence","invalid-choice","Choose a supported confidence."));
        if(rule.Id==Guid.Empty || rule.Version<1 || rule.DuplicateQuotePolicy is not ("allow-competing" or "broker-of-record" or "refer"))
            issues.Add(new("/rule","invalid-rule","Pin an existing version and supported policy."));
        var ruleSummary=Text(rule.Summary,1000,"/rule/summary",issues);
        if(signals is null || signals.Count is <1 or >100)issues.Add(new("/signals","invalid-count","Supply between one and 100 comparison signals."));
        var normalized=new List<MatchSignal>();
        if(signals is {Count:<=100})foreach(var signal in signals)
        {
            if(signal is null){issues.Add(new("/signals","invalid-signal","Supply a comparison signal."));continue;}
            var code=Text(signal.Code,100,"/signals/code",issues);var summary=Text(signal.Summary,500,"/signals/summary",issues);
            var submitted=Text(signal.SubmittedValue,500,"/signals/submittedValue",issues);var candidate=Text(signal.CandidateValue,500,"/signals/candidateValue",issues);
            if(signal.Weight is not ("definitive" or "strong" or "moderate" or "weak") || signal.Result is not ("match" or "near-match" or "different" or "cannot-compare"))
                issues.Add(new("/signals","invalid-choice","Choose supported signal weight and comparison result."));
            normalized.Add(new(code,summary,submitted,candidate,signal.Weight,signal.Result));
        }
        if(normalized.Select(x=>x.Code).Distinct(StringComparer.Ordinal).Count()!=normalized.Count)issues.Add(new("/signals/code","duplicate","Signal codes must be distinct."));
        if(issues.Count>0)throw new PartyValidationException(issues);
        return new(new(client.LegalName,client.EntityType,client.Address,client.CompanyNumber),normalized.ToArray(),rule with {Summary=ruleSummary},confidence);
    }

    private static string Text(string? value,int limit,string path,List<PartyFieldIssue> issues)
    {
        if(string.IsNullOrWhiteSpace(value)){issues.Add(new(path,"required","Enter a value."));return "";}
        if(value.Length>limit)issues.Add(new(path,"too-long",$"Use at most {limit} characters."));
        if(value.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))issues.Add(new(path,"invalid-text","Remove unsupported control characters."));
        return value.Replace("\r\n","\n").Replace('\r','\n').Trim();
    }
}
