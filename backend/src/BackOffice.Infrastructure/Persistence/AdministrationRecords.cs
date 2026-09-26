namespace BackOffice.Infrastructure.Persistence;

// Secrets are protected separately from public command receipts and audit.
public sealed class IdentityAction : MutableRecord
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public byte[] TokenHash { get; set; } = [];
    public byte[]? SecretCiphertext { get; set; }
    public string SecurityStamp { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int Attempts { get; set; }
}
