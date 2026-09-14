namespace BackOffice.Infrastructure.Persistence;

public sealed class AgencyInvitation:MutableRecord
{
    public Guid AgencyId {get;set;}
    public Guid UserId {get;set;}
    public string State {get;set;}="staged";
    public byte[]? TokenHash {get;set;}
    public DateTimeOffset? IssuedAt {get;set;}
    public DateTimeOffset? ExpiresAt {get;set;}
    public Guid? NotificationId {get;set;}
    public DateTimeOffset? AcceptedAt {get;set;}
    public DateTimeOffset? RevokedAt {get;set;}
}
