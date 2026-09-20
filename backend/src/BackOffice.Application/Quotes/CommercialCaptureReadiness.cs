using System.Globalization;
using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

// Pure capture completeness. Rating, evidence, referral decisions and authority
// remain independent gates. Applicable questions use the pinned source catalogue.
public static class CommercialCaptureReadiness
{
    private sealed record Question(string Id, string Container, string Label, string Stage);
    private static readonly Lazy<Question[]> Questions = new(LoadQuestions);
    private static readonly string[] SubsidenceQuestions = ["prototype.quote.aaccb5c97c33", "prototype.quote.8293bc041f81", "prototype.quote.02d9c6be528a", "prototype.quote.a6a4579c177e", "prototype.quote.7a732bc99a5f", "prototype.quote.fcc7e22c33ea", "prototype.quote.923e600eb3a4"];
    public static int QuestionCount => Questions.Value.Length;

    public static IReadOnlyList<QuoteReadinessIssue> Assess(JsonElement proposal, DateOnly asOf)
    {
        var shape = QuoteCaptureShape.ValidateCommercial(proposal);
        if (shape.Count > 0) return shape.Select(x => Issue(x.Path, x.Code, "Correct the commercial proposal shape.")).ToArray();
        var issues = new List<QuoteReadinessIssue>();
        var risk = At(proposal, "risk"); var business = At(risk, "business"); var cover = At(proposal, "cover");
        var locations = Items(At(risk, "locations")).ToArray(); var wages = Items(At(risk, "wages")).ToArray();
        var losses = Items(At(risk, "losses")).ToArray(); var liability = At(risk, "liability");
        void Required(JsonElement owner, string path, params string[] fields)
        {
            foreach (var field in fields)
                if (!Present(At(owner, field))) issues.Add(Issue(path + "/" + field.Replace('.', '/'), "commercial-field-required", "Complete this commercial insurance detail."));
        }
        Required(proposal, "", "insured.entityType", "insured.legalName", "insured.proposerNames", "insured.address.line1", "insured.address.town", "insured.address.postcode", "insured.address.country", "insured.contact.email");
        if (!Items(At(proposal, "insured.proposerNames")).Any()) issues.Add(Issue("/insured/proposerNames", "commercial-proposer-required", "Add a proposer name."));
        if (Text(At(proposal, "insured.entityType")) == "limited-company") Required(proposal, "", "insured.companyNumber");
        Required(business, "/risk/business", "description", "startedOn", "vatRegistered", "turnover");
        if (Date(Text(At(business, "startedOn")), out var started) && started > asOf)
            issues.Add(Issue("/risk/business/startedOn", "business-start-after-assessment", "The business start date is in the future."));
        var activities = Items(At(business, "activities")).ToArray();
        if (activities.Length == 0) issues.Add(Issue("/risk/business/activities", "commercial-activity-required", "Add the business activities."));
        for (var i = 0; i < activities.Length; i++) Required(activities[i], $"/risk/business/activities/{i}", "code", "description", "percentageBasisPoints");
        if (activities.Length > 0 && activities.All(x => Present(At(x, "percentageBasisPoints"))) && activities.Sum(x => Number(At(x, "percentageBasisPoints")) ?? 0) != 10000)
            issues.Add(Issue("/risk/business/activities", "activity-total-must-be-10000", "Activity percentages must total 100%."));
        if (locations.Length == 0) issues.Add(Issue("/risk/locations", "commercial-location-required", "Add an insured location."));
        for (var i = 0; i < locations.Length; i++)
        {
            var row = locations[i]; var path = $"/risk/locations/{i}";
            Required(row, path, "reference", "address.line1", "address.town", "address.postcode", "address.country", "occupancy", "buildings", "contents", "stock", "sprinklers", "floodZone");
            if (new[] { "buildings", "contents", "stock" }.All(x => Present(At(row, x))) && Total(row) <= 0)
                issues.Add(Issue(path, "location-positive-sum-insured-required", "A location needs a positive insured value."));
            if (Text(At(row, "address.postcode")) is { } postcode && CommercialCaptureRules.NormalizePostcode(postcode) is null)
                issues.Add(Issue(path + "/address/postcode", "location-postcode-invalid", "Enter a valid UK postcode."));
            if (Text(At(row, "floodZone")) == "unknown") issues.Add(Issue(path + "/floodZone", "location-flood-zone-unknown", "Resolve the location's flood zone before progression."));
            if (Money(At(row, "maximumEstimatedLoss")) > Total(row)) issues.Add(Issue(path + "/maximumEstimatedLoss", "location-mel-exceeds-total", "The supplied maximum estimated loss exceeds the location sum insured."));
        }
        Required(liability, "/risk/liability", "publicLimit", "productsLimit", "maximumHeightMetres");
        var el = Boolean(Response(At(risk, "declarations"), "prototype.quote.36ef01068295"));
        if (el == true)
        {
            Required(liability, "/risk/liability", "employersLimit", "employersReferenceNumber");
            if (Money(At(liability, "employersLimit")) <= 0 || wages.Sum(x => Money(At(x, "employees")) + Money(At(x, "labourOnlySubcontractors"))) <= 0)
                issues.Add(Issue("/risk/wages", "el-details-required", "Employers’ liability needs a positive limit and employee or labour-only wages."));
        }
        if (el == false && (Money(At(liability, "employersLimit")) > 0 || Present(At(liability, "employersReferenceNumber"))))
            issues.Add(Issue("/risk/liability", "el-disabled-details", "Review the retained employers’ liability details or select this cover."));
        for (var i = 0; i < wages.Length; i++) Required(wages[i], $"/risk/wages/{i}", "category", "employees", "labourOnlySubcontractors", "bonaFideSubcontractors");
        if (Boolean(Response(At(risk, "declarations"), "prototype.quote.8cc83c3a006f")) == true)
            Required(liability, "/risk/liability", "hotWorksProcedures");
        var bi = At(risk, "businessInterruption"); var biSelected = Boolean(Response(At(cover, "responses"), "prototype.quote.7660fc5eb42e"));
        if (biSelected == true)
        {
            Required(bi, "/risk/businessInterruption", "basis", "sumInsured", "indemnityMonths", "declarationLinked");
            if (Money(At(bi, "sumInsured")) <= 0) issues.Add(Issue("/risk/businessInterruption/sumInsured", "bi-details-required", "Business interruption needs a positive sum insured."));
            var dependencies = Items(At(bi, "dependencies")).ToArray();
            for (var i = 0; i < dependencies.Length; i++) Required(dependencies[i], $"/risk/businessInterruption/dependencies/{i}", "kind", "name", "limit");
        }
        if (biSelected == false && bi.ValueKind == JsonValueKind.Object && bi.EnumerateObject().Any())
            issues.Add(Issue("/risk/businessInterruption", "bi-disabled-details", "Review the retained business interruption details or select this cover."));
        var cw = At(cover, "contractWorks"); Required(cw, "/cover/contractWorks", "selected");
        if (Boolean(At(cw, "selected")) == true)
        {
            Required(cw, "/cover/contractWorks", "sumInsured", "excess");
            if (Money(At(cw, "sumInsured")) <= 0) issues.Add(Issue("/cover/contractWorks/sumInsured", "contract-works-details-required", "Contract works needs a positive sum insured."));
        }
        if (Boolean(At(cw, "selected")) == false && (Present(At(cw, "sumInsured")) || Present(At(cw, "excess"))))
            issues.Add(Issue("/cover/contractWorks", "contract-works-disabled-details", "Review the retained contract works details or select this cover."));
        var lossDeclared = Boolean(Response(At(risk, "declarations"), "prototype.quote.0b0b63fca324"));
        if (lossDeclared == true && losses.Length == 0) issues.Add(Issue("/risk/losses", "commercial-loss-required", "Add the declared loss history."));
        if (lossDeclared == false && losses.Length > 0) issues.Add(Issue("/risk/losses", "commercial-loss-declaration-conflict", "Recorded losses conflict with the No answer."));
        for (var i = 0; i < losses.Length; i++)
        {
            var path = $"/risk/losses/{i}"; Required(losses[i], path, "occurredOn", "type", "amount", "paid", "reserve", "status", "description");
            if (Date(Text(At(losses[i], "occurredOn")), out var date) && date > asOf) issues.Add(Issue(path + "/occurredOn", "loss-date-after-assessment", "The loss date is in the future."));
        }
        foreach (var q in Questions.Value)
        {
            if (!Applicable(q, proposal)) continue;
            foreach (var (responses, path) in Subjects(proposal, q.Container))
                if (!Present(Response(responses, q.Id))) issues.Add(new(path + "/answers", "commercial-question-required", "Answer: " + q.Label, "capture", "error", q.Id));
        }
        var machinery = Money(Response(At(cover, "responses"), "prototype.quote-value.5743fa7db272"));
        var computers = Money(Response(At(cover, "responses"), "prototype.quote-value.fa63248f9ae1"));
        if (locations.All(x => Present(At(x, "contents"))) && machinery + computers > locations.Sum(x => Money(At(x, "contents"))))
            issues.Add(Issue("/cover/responses", "contents-breakdown-exceeds-total", "Machinery and computers exceed the declared contents sum insured."));
        return issues;
    }

    private static bool Applicable(Question q, JsonElement proposal)
    {
        var declarations = At(proposal, "risk.declarations"); var cover = At(proposal, "cover.responses");
        bool Yes(string id) => Boolean(Response(declarations, id)) == true;
        long? Choice(string id) => Number(At(Response(declarations, id), "value"));
        bool AnyStage(string stage, bool answer, params string[] exclude) => Questions.Value.Where(x => x.Stage == stage && x.Container == "risk.declarations" && !exclude.Contains(x.Id)).Any(x => Boolean(Response(declarations, x.Id)) == answer);
        if (SubsidenceQuestions.Contains(q.Id)) return Yes("prototype.quote.be47c08f530f");
        return q.Id switch
        {
            "prototype.quote.7bd824326d4c" or "prototype.quote.9b4688f28580" or "prototype.quote.72cb884b6df1" or "prototype.quote.a2f1dd35162a" or "prototype.quote.ab908171e40b" => Boolean(Response(cover, "prototype.quote.7660fc5eb42e")) == true,
            "prototype.quote.4a288359be03" => Yes("prototype.quote.be47c08f530f"),
            "prototype.quote-value.b99b3a5b3004" => AnyStage("Commercial Combined:step-2", true, "prototype.quote.36ef01068295"),
            "prototype.quote-value.4799b8daa1ca" => Choice("prototype.quote.ade0f3f0df5e") is 2 or 3 || Yes("prototype.quote.c08c9ebaf825") || Yes("prototype.quote.a6ff9fdbe769") || Yes("prototype.quote.be47c08f530f") && SubsidenceQuestions.Any(Yes),
            "prototype.quote-value.a0e5d1b910f8" => AnyStage("Commercial Combined:step-9", false)
                || Choice("prototype.quote.00fa2758dd8c") is 2 or 3
                || Choice("prototype.quote.6d9a54d9e464") == 2
                || Choice("prototype.quote.956ebc71fded") is 2 or 3
                || Choice("prototype.quote.992fa2724da2") == 2
                || Choice("prototype.quote.ca115cf242f7") == 2
                || Choice("prototype.quote.978fde66afb5") == 3,
            "prototype.quote-value.5fcb5378a1fd" => Yes("prototype.quote.c70fcdf94e4e"),
            "prototype.quote-value.59c91e8db152" => Items(At(proposal, "risk.locations")).Any(x => Boolean(Response(At(x, "responses"), "prototype.addloc.sole-occupier")) == false),
            "prototype.quote-value.acace76b1053" => Items(At(proposal, "risk.locations")).Any(x => Boolean(Response(At(x, "responses"), "prototype.addloc.detached")) == true),
            // No previous insurance is possible; the source has no controlling selector.
            "prototype.quote-value.7adfff3ade76" => false,
            _ => true
        };
    }

    private static IEnumerable<(JsonElement Responses, string Path)> Subjects(JsonElement proposal, string container)
    {
        var split = container.IndexOf("[]", StringComparison.Ordinal);
        if (split < 0) { yield return (At(proposal, container), "/" + container.Replace('.', '/')); yield break; }
        var prefix = container[..split]; var suffix = container[(split + 3)..]; var index = 0;
        foreach (var row in Items(At(proposal, prefix))) yield return (At(row, suffix), "/" + prefix.Replace('.', '/') + "/" + index++ + "/" + suffix.Replace('.', '/'));
    }
    private static JsonElement Response(JsonElement responses, string id) => Items(At(responses, "answers")).Where(x => Text(At(x, "questionId")) == id).Select(x => At(x, "value")).FirstOrDefault();
    private static decimal Money(JsonElement value) => decimal.TryParse(Text(value), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
    private static decimal Total(JsonElement row) => Money(At(row, "buildings")) + Money(At(row, "contents")) + Money(At(row, "stock"));
    private static QuoteReadinessIssue Issue(string path, string code, string message) => new(path, code, message, "capture", "error");
    private static Question[] LoadQuestions()
    {
        using var stream = typeof(CommercialCaptureReadiness).Assembly.GetManifestResourceStream("QuoteCapture.Questions") ?? throw new InvalidOperationException("Commercial question source is missing.");
        using var doc = JsonDocument.Parse(stream);
        var questions = doc.RootElement.GetProperty("deferredQuestions").EnumerateArray().Where(x => Items(At(x, "products")).Any(p => p.GetString() == CommercialCaptureRules.ProductCode))
            .Select(x => new Question(x.GetProperty("questionId").GetString()!, x.GetProperty("targetContainer").GetString()!, x.GetProperty("label").GetString()!, Text(Items(At(x, "stages")).FirstOrDefault()) ?? "item-dialog")).ToArray();
        if (questions.Length != 109 || questions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != 109)
            throw new InvalidOperationException("The pinned commercial question source does not match its published version.");
        return questions;
    }
}
