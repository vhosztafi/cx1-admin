namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingAcceptance : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public Guid TermsVersionId { get; set; }
    public Guid DeliveryId { get; set; }
    public string TermsHash { get; set; }="";
    public string AssuranceHash { get; set; }="";
    public string AccepterLabel { get; set; }="";
    public DateTimeOffset AcceptedAt { get; set; }
    public string Channel { get; set; }="";
    public Guid EvidenceAssociationId { get; set; }
    public Guid EvidenceReviewId { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
