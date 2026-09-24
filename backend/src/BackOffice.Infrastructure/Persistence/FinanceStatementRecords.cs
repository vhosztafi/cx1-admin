namespace BackOffice.Infrastructure.Persistence;

// A statement is a sealed version of one agency debtor's posted movement book.
// RowsJson and ContentBytes are saved at generation; downloads never rebuild them.
public sealed class FinanceStatementVersion : StoredRecord
{
    public Guid AgencyId { get; set; }
    public int Version { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTimeOffset SourceCutoff { get; set; }
    public Guid? AgencyTermsVersionId { get; set; }
    public string SourceIdsJson { get; set; } = "[]";
    public byte[] SourceHash { get; set; } = [];
    public string SnapshotJson { get; set; } = "{}";
    public string Opening { get; set; } = "0.00";
    public string Debits { get; set; } = "0.00";
    public string Credits { get; set; } = "0.00";
    public string Closing { get; set; } = "0.00";
    public byte[] ContentBytes { get; set; } = [];
    public byte[] ContentHash { get; set; } = [];
}
