namespace BackOffice.Infrastructure.Persistence;

// Persistence records stay inside Infrastructure. Domain services do not depend on EF.
public abstract class StoredRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public abstract class MutableRecord : StoredRecord
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class StaffUser : MutableRecord
{
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string State { get; set; } = "active";
    public Guid? TeamId { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Team : MutableRecord { public string Name { get; set; } = ""; }
public sealed class Role : MutableRecord
{
    public string Code { get; set; } = "";
    public string Scope { get; set; } = "internal";
}
public sealed class UserRole : MutableRecord
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}
public sealed class UserCredential : MutableRecord
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "local";
    public string ProviderSubject { get; set; } = "";
    public string? PasswordHash { get; set; }
    public byte[]? MfaSecretCiphertext { get; set; }
    public bool MustReset { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
public sealed class UserSession : MutableRecord
{
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string DeviceLabel { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public byte[] TicketCiphertext { get; set; } = [];
}
public sealed class CapacityProvider : MutableRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "active";
}
public sealed class Product : MutableRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class ProductVersion : MutableRecord
{
    public Guid ProductId { get; set; }
    public int Version { get; set; }
    public string State { get; set; } = "draft";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public Guid ProviderId { get; set; }
    public string JsonSchemaVersion { get; set; } = "";
    public string QuestionSetVersion { get; set; } = "";
    public string Definition { get; set; } = "{}";
}
public sealed class SettingVersion : StoredRecord
{
    public string Scope { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public string Values { get; set; } = "{}";
}
public sealed class DemoClock : MutableRecord
{
    public string Name { get; set; } = "demo";
    public DateTimeOffset? FrozenAt { get; set; }
}
public sealed class AuditEvent : StoredRecord
{
    public Guid? ActorId { get; set; }
    public Guid? SubjectRecordId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Reason { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public Guid CorrelationId { get; set; }
}
public sealed class OutboxWork : MutableRecord
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public Guid? SubjectRecordId { get; set; }
    public string OperationKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string State { get; set; } = "pending";
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public Guid? ScenarioVersionId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public int Attempts { get; set; }
    public string? Result { get; set; }
}
public sealed class DiagnosticReceipt : StoredRecord
{
    public Guid WorkId { get; set; }
    public Guid ProviderOperationId { get; set; }
    public string Reference { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}
public sealed class AdapterQuarantine : StoredRecord
{
    public Guid InboxId { get; set; }
    public byte[] ObservedHash { get; set; } = [];
    public DateTimeOffset ReceivedAt { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class JobException : StoredRecord
{
    public Guid WorkId { get; set; }
    public string Code { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class AdapterAttempt : MutableRecord
{
    public Guid WorkId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Outcome { get; set; } = "started";
    public string Request { get; set; } = "{}";
    public string? Response { get; set; }
    public string? ErrorCode { get; set; }
}
public sealed class DemoProviderOperation : MutableRecord
{
    public string Kind { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public Guid ScenarioVersionId { get; set; }
    public string State { get; set; } = "pending";
    public string? Result { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
public sealed class AdapterInbox : MutableRecord
{
    public string Provider { get; set; } = "";
    public string EventId { get; set; } = "";
    public byte[] ContentHash { get; set; } = [];
    public Guid WorkId { get; set; }
    public string State { get; set; } = "received";
    public DateTimeOffset? AppliedAt { get; set; }
    public string? QuarantineReason { get; set; }
}
public sealed class IdempotencyRecord : StoredRecord
{
    public string ActorScope { get; set; } = "";
    public string Route { get; set; } = "";
    public string Key { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public int ResultStatus { get; set; }
    public string ResultBody { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; }
}
