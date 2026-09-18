namespace BackOffice.Infrastructure.Persistence;

public sealed class RenewalLapseEvent : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid RuleSettingVersionId { get; set; }
    public Guid WorkId { get; set; }
    public string Mode { get; set; } = "manual";
    public string Reason { get; set; } = "";
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset AutoLapseAt { get; set; }
    public string RecipientSnapshotJson { get; set; } = "[]";
}

public sealed class RenewalLapseNotificationReceipt : StoredRecord
{
    public Guid LapseEventId { get; set; }
    public Guid WorkId { get; set; }
    public string Outcome { get; set; } = "demo-delivered";
    public byte[] PayloadHash { get; set; } = [];
}
