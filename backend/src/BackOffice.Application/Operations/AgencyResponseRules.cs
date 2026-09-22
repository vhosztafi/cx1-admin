namespace BackOffice.Application.Operations;

public static class AgencyResponseRules
{
    public static void Track(string deliveryState, string body, string reason)
    {
        if(deliveryState != "delivered" || string.IsNullOrWhiteSpace(body) || body.Length > 8000)
            throw new CommunicationRuleException("agency-request-not-delivered");
        Reason(reason);
    }

    public static void Resolve(string state, string outcome, string reason)
    {
        if(state != "awaiting-response" || outcome is not ("response-received" or "withdrawn"))
            throw new CommunicationRuleException("invalid-agency-response-transition");
        Reason(reason);
    }

    private static void Reason(string reason)
    {
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10 || reason.Length > 2000)
            throw new CommunicationRuleException("agency-response-reason-required");
    }
}
