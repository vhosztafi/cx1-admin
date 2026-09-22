namespace BackOffice.Infrastructure.Persistence;

// Additive evidence: original consequence payloads, receipt identities and files remain intact.
public sealed class CancellationOperationalReceipt : StoredRecord
{
    public Guid ConsequenceId { get; set; }
    public Guid WorkId { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string Outcome { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
}

public sealed class CancellationNoticeDispatch : StoredRecord
{
    public Guid ConsequenceId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public Guid DeliveryId { get; set; }
    public string PayloadHash { get; set; } = "";
}

// Term-level withdrawal also applies to a later render of an historical certificate.
public sealed class CertificateWithdrawal : StoredRecord
{
    public Guid ConsequenceId { get; set; }
    public Guid TermId { get; set; }
    public string CertificateKind { get; set; } = "";
    public DateTimeOffset EffectiveAt { get; set; }
}

public sealed class CancellationTaskClosure : StoredRecord
{
    public Guid ConsequenceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskEventId { get; set; }
}
