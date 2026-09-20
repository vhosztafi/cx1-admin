namespace BackOffice.Application.Policies;

public static class CommercialDocumentSelection
{
    // The certificate template is product-specific. It represents EL for CC;
    // Motor Trade keeps its existing independent certificate selection.
    public static string[] Kinds(bool employersLiabilitySelected) => employersLiabilitySelected
        ? ["policy-schedule", "policy-certificate", "policy-statement"]
        : ["policy-schedule", "policy-statement"];
}
