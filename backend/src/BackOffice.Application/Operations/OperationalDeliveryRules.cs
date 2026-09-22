namespace BackOffice.Application.Operations;

public sealed record DocumentPackWrite(Guid[] DocumentVersionIds,Guid[] RecipientContactIds,string Subject,string Body);
public static class OperationalDeliveryRules
{
    public static void Message(string visibility,string subject,MessageDraftWrite input)
    {
        CommunicationRules.Draft(input,visibility);
        if(visibility!="agency"||string.IsNullOrWhiteSpace(subject)||subject.Length>300||
            string.IsNullOrWhiteSpace(input.Body)||input.RecipientContactIds.Length==0)
            throw new CommunicationRuleException("delivery-not-ready");
    }
    public static void Pack(DocumentPackWrite input)
    {
        if(input is null||input.DocumentVersionIds is null||input.DocumentVersionIds.Length==0)
            throw new CommunicationRuleException("pack-documents-required");
        Message("agency",input.Subject,new(input.Body,input.RecipientContactIds,input.DocumentVersionIds));
    }
}
