using System.Net.Mail;

namespace BackOffice.Application.Operations;

public sealed record ThreadWrite(string Visibility,string Subject,Guid? RelationshipId=null);
public sealed record MessageDraftWrite(string Body,Guid[] RecipientContactIds,Guid[] AttachmentVersionIds);
public sealed class CommunicationRuleException(string code):Exception("Check the communication details.")
{
    public string Code {get;}=code;
}
public static class CommunicationRules
{
    public static void Note(string? body)=>Text(body,8000,"invalid-note");
    public static void Thread(ThreadWrite input)
    {
        if(input is null)throw Invalid("invalid-thread");
        Text(input.Subject,300,"invalid-thread-subject");
        if(input.Visibility is not ("internal" or "agency") ||
            (input.Visibility=="internal" && input.RelationshipId is not null) ||
            (input.Visibility=="agency" && (input.RelationshipId is null || input.RelationshipId==Guid.Empty)))
            throw Invalid("invalid-thread-audience");
    }
    public static void Draft(MessageDraftWrite input,string visibility)
    {
        if(input is null || input.Body is null || input.Body.Length>8000)throw Invalid("invalid-message-body");
        Ids(input.RecipientContactIds,50,"invalid-message-recipients");
        Ids(input.AttachmentVersionIds,20,"invalid-message-attachments");
        if(visibility is not ("internal" or "agency") || visibility=="internal" && input.RecipientContactIds.Length!=0)
            throw Invalid("invalid-message-audience");
    }
    public static bool Email(string? email)=>email is {Length:>0 and <=254} && !email.Any(char.IsControl) &&
        MailAddress.TryCreate(email,out var address) && address.Address==email && address.DisplayName.Length==0;
    private static void Ids(Guid[]? ids,int maximum,string code)
    {
        if(ids is null || ids.Length>maximum || ids.Any(x=>x==Guid.Empty) || ids.Distinct().Count()!=ids.Length)throw Invalid(code);
    }
    private static void Text(string? value,int maximum,string code)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>maximum)throw Invalid(code);
    }
    private static CommunicationRuleException Invalid(string code)=>new(code);
}
