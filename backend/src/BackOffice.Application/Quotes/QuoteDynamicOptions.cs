using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteDynamicOptionsResult(IReadOnlyDictionary<string,string[]> SelectedCollections, IReadOnlyList<QuoteFieldIssue> Issues);

public static partial class QuoteDriverRules
{
    public static QuoteDynamicOptionsResult AssessDynamic(JsonElement proposal, bool requireDriverAnswers = true)
    {
        var selected = new Dictionary<string,string[]>(); var issues = new List<QuoteFieldIssue>();
        void Add(string code,string path,string? question = null) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,path,question)); }
        void Select(JsonElement reference,string path,string collection,string question)
        {
            selected[path] = [collection];
            if (Text(At(reference,"collection")) != collection) Add("reference-collection-mismatch",path + "/collection",question);
        }
        var cover = QuoteCoverFacts.Reconcile(proposal);
        long? level = cover.Facts.GetValueOrDefault("coverLevel") switch { "comprehensive" => 1, "third-party-fire-theft" => 2, "third-party-only" => 3, _ => null };
        JsonElement Equivalent(string fact,string collection) => cover.Facts.TryGetValue(fact,out var value)
            ? Items(QuoteReferenceMetadata.Collection(collection)).FirstOrDefault(row => QuoteReferenceMetadata.Numeric(row)?.ToString("F2",CultureInfo.InvariantCulture) == value) : default;
        var own = Equivalent("ownVehicleLimit","indemnityOwnVehicles"); var customer = Equivalent("customerVehicleLimit","indemnityCustomerVehicles");
        var ownAmount = QuoteReferenceMetadata.Numeric(own); var customerAmount = QuoteReferenceMetadata.Numeric(customer);
        var validOwn = ownAmount is not null && level is not null && (level != 2 || ownAmount <= 15000);
        var coverAnswers = At(proposal,"cover.responses");
        var excess = Answer(coverAnswers,"/cover/responses","MTS-05-Q04");
        var ownExcessCollection = own.ValueKind == JsonValueKind.Undefined ? null : $"indemnityOwnVehicles/number:{Number(At(own,"value"))}/excesses";
        if (excess.Value.ValueKind != JsonValueKind.Undefined)
        {
            if (level == 3) Add("inactive-dynamic-answer",excess.Path,"MTS-05-Q04");
            else if (!validOwn) Add("missing-dynamic-dependency",excess.Path,"MTS-05-Q04");
            else Select(excess.Value,excess.Path,ownExcessCollection!,"MTS-05-Q04");
        }
        var prototypeExcess = Answer(coverAnswers,"/cover/responses","prototype.quote.00216de47ab5");
        if (prototypeExcess.Value.ValueKind != JsonValueKind.Undefined)
        {
            if (level == 3) Add("inactive-dynamic-answer",prototypeExcess.Path,"prototype.quote.00216de47ab5");
            else if (!validOwn) Add("missing-dynamic-dependency",prototypeExcess.Path,"prototype.quote.00216de47ab5");
            else if (Equivalent("excess",ownExcessCollection!).ValueKind == JsonValueKind.Undefined) Add("unsupported-cover-configuration",prototypeExcess.Path,"prototype.quote.00216de47ab5");
        }
        foreach (var (fact,question,row) in new[] { ("ownVehicleLimit","prototype.quote.d9dd069a314c",own), ("customerVehicleLimit","prototype.quote.b4c7e25f7781",customer) })
        {
            var entry = Answer(coverAnswers,"/cover/responses",question);
            if (entry.Value.ValueKind != JsonValueKind.Undefined && cover.Facts.ContainsKey(fact) && row.ValueKind == JsonValueKind.Undefined) Add("unsupported-cover-configuration",entry.Path,question);
        }
        var activities = Items(At(proposal,"risk.business.activities")).Select(row => TrustedMetadata(At(row,"code"),"mtOccupations")).ToArray();
        var completeActivities = activities.Length > 0 && activities.All(row => row.ValueKind != JsonValueKind.Undefined);
        var customerRequired = completeActivities && activities.Any(row => At(row,"customerLOI").ValueKind == JsonValueKind.True);
        var validCustomer = customerAmount is not null && level is not null && (level != 2 || customerAmount is > 0 and <= 15000);
        decimal? limit = validOwn && completeActivities && (!customerRequired || level == 3 || validCustomer)
            ? customerRequired && level != 3 ? Math.Max(ownAmount!.Value,customerAmount!.Value) : ownAmount : null;
        var bands = QuoteReferenceMetadata.Root.GetProperty("youngDriverConfiguration").EnumerateArray().ToArray();
        bool? Both(bool? left,bool? right) => left == false || right == false ? false : left is null || right is null ? null : true;
        JsonElement[]? Eligible(JsonElement rows) => limit is null ? null : Items(rows).Where(row => QuoteReferenceMetadata.Numeric(row) is > 0 and var amount && amount <= limit).ToArray();
        var index = 0;
        foreach (var driver in Items(At(proposal,"risk.drivers")))
        {
            var root = $"/risk/drivers/{index++}/responses"; var responses = At(driver,"responses"); var start = At(proposal,"termIntent.localStartDate");
            var age = Years(At(driver,"dateOfBirth"),start); var experience = Years(At(driver,"licence.issuedOn"),start);
            var band = age is null ? -1 : Array.FindIndex(bands,row => (Number(At(row,"ageFrom")) ?? 0) <= age && age <= (Number(At(row,"ageTo")) ?? 1000));
            bool? young = age is null ? null : age < 25;
            bool? inexperienced = age is null || experience is null ? null : age >= 25 && experience < 1;
            bool? limited = level is null ? null : level != 3;
            var indemnities = band < 0 ? null : Eligible(At(bands[band],"indemnities"));
            var excesses = Eligible(QuoteReferenceMetadata.Collection("driverExperienceBasedExcesses"));
            if (requireDriverAnswers)
            {
                var conditions = new (int Question,bool? Active)[] {
                    (58,Both(young,limited)), (59,Both(Both(young,limited),indemnities is null ? null : indemnities.Length > 0)),
                    (60,young), (61,Both(Both(inexperienced,limited),excesses is null ? null : excesses.Length > 0)), (62,inexperienced)
                };
                foreach (var (number,active) in conditions)
                {
                    var entry = Answer(responses,root,Id(number)); var present = entry.Value.ValueKind != JsonValueKind.Undefined;
                    if (active is null) Add("driver-option-context-required",entry.Path,Id(number));
                    else if (active == true && !present) Add("driver-option-answer-required",entry.Path,Id(number));
                    else if (active == false && present) Add("inactive-driver-option-retained",entry.Path,Id(number));
                }
                var entryExcess = Answer(responses,root,Id(61)); var selectedExcess = QuoteCatalogueIdentity.TrustedValue(entryExcess.Value,"driverExperienceBasedExcesses");
                if (inexperienced == true && limited == true && excesses is not null && selectedExcess is not null &&
                    !excesses.Any(row => Number(At(row,"value")) == selectedExcess)) Add("experience-excess-exceeds-policy-limit",entryExcess.Path,Id(61));
            }
            foreach (var (number,child) in new[] { (59,"indemnities"), (60,"cCs") })
            {
                var entry = Answer(responses,root,Id(number)); if (entry.Value.ValueKind == JsonValueKind.Undefined) continue;
                if (age is >= 25 || child == "indemnities" && level == 3) { Add("inactive-dynamic-answer",entry.Path,Id(number)); continue; }
                if (band < 0 || child == "indemnities" && (limit is null || level is null)) { Add("missing-dynamic-dependency",entry.Path,Id(number)); continue; }
                var collection = $"youngDriverConfiguration/{band}/{child}"; Select(entry.Value,entry.Path,collection,Id(number));
                if (child == "indemnities")
                {
                    var row = TrustedMetadata(entry.Value,collection);
                    if (row.ValueKind != JsonValueKind.Undefined && (QuoteReferenceMetadata.Numeric(row) is null or 0 || QuoteReferenceMetadata.Numeric(row) > limit)) Add("indemnity-exceeds-policy-limit",entry.Path,Id(number));
                }
            }
        }
        return new(selected,issues.Concat(cover.Issues).Take(QuoteCaptureShape.MaximumIssues).ToArray());
    }
}
