namespace BackOffice.Application.Policies;

public sealed record ServicingLeaseState(Guid HolderId, Guid Token, int Generation, DateTimeOffset ExpiresAt, bool Active);

// A token fences a particular edit generation; it never grants identity or
// policy access. Services must hold current scope and the draft lock/ETag.
public static class ServicingLeaseRules
{
    public static ServicingLeaseState Acquire(ServicingLeaseState? previous, Guid actorId, DateTimeOffset now,
        bool takeover, bool canTakeover, string? reason)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("A current holder identity is required.");
        if (takeover && (!canTakeover || reason?.Trim().Length is not (>= 10 and <= 2000)))
            throw new ArgumentException("Takeover requires current authority and a bounded reason.");
        if (previous is { Active: true } && previous.ExpiresAt > now && previous.HolderId != actorId && !takeover)
            throw new ArgumentException("Another editor holds the live lease.");
        var generation = checked((previous?.Generation ?? 0) + 1);
        return new(actorId, Guid.NewGuid(), generation, now.AddMinutes(5), true);
    }

    public static void Demand(ServicingLeaseState lease, Guid actorId, Guid token, DateTimeOffset now)
    {
        if (!lease.Active || actorId == Guid.Empty || token == Guid.Empty || lease.HolderId != actorId ||
            lease.Token != token || lease.ExpiresAt <= now)
            throw new ArgumentException("Lease expired, released or replaced; retain local edits and reload ownership.");
    }

    public static ServicingLeaseState Renew(ServicingLeaseState lease, Guid actorId, Guid token, DateTimeOffset now)
    {
        Demand(lease, actorId, token, now);
        return lease with { ExpiresAt = now.AddMinutes(5) };
    }

    public static ServicingLeaseState Release(ServicingLeaseState lease, Guid actorId, Guid token, DateTimeOffset now)
    {
        Demand(lease, actorId, token, now);
        return lease with { Active = false, ExpiresAt = now };
    }
}
