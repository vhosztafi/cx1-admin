using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record IncidentOccurrenceCandidate(Guid VersionId,string SourceHash,DateTimeOffset From,DateTimeOffset To,string Label);
public sealed record IncidentOccurrenceResolution(Guid PolicyId,DateTimeOffset KnownAt,string State,IncidentOccurrenceWindow? Window,IReadOnlyList<IncidentOccurrenceCandidate> Candidates,string? SourceHash);

// Produces a value for the owning immutable incident revision. The incident command
// persists that revision binding in09-12; callers cannot replace temporal candidates
// with a mutable current policy pointer or a caller-supplied version ID.
public sealed class IncidentOccurrenceResolver(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<IncidentOccurrenceResolution> Resolve(ActorContext actor,Guid policyId,IncidentOccurrence? occurrence,DateTimeOffset? knownAt=null,CancellationToken token=default,JsonElement? subject=null)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var result=await ResolveHeld(db,actor,policyId,occurrence,knownAt??time.GetUtcNow(),token,subject);
        await transaction.CommitAsync(token);return result;
    }
    public async Task<IncidentOccurrenceResolution> ResolveHeld(BackOfficeDbContext db,ActorContext actor,Guid policyId,IncidentOccurrence? occurrence,DateTimeOffset knownAt,CancellationToken token,JsonElement? subject=null)
    {
        if(knownAt>time.GetUtcNow())throw new IncidentOccurrenceException("occurrence-knowledge-future");
        await OperationalScope.HoldParents(db,actor,[new("policy",policyId)],"incident-write",token);
        if(occurrence is null)return new(policyId,knownAt,"incomplete",null,[],null);
        var window=IncidentOccurrenceRules.Window(occurrence);
        var history=await PolicyTemporalSelector.Candidates(db,policyId,knownAt).ToArrayAsync(token);
        var intervals=Intervals(history,policyId,window,knownAt);
        var ids=intervals.Select(x=>x.Candidate.VersionId).Distinct().ToArray();
        var versions=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.PolicyId==policyId&&ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        var candidates=new List<IncidentOccurrenceCandidate>();
        foreach(var (candidate,from,to) in intervals)
        {
            if(!versions.TryGetValue(candidate.VersionId,out var version)||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(version.SnapshotJson)),version.ContentHash))
                throw new IncidentOccurrenceException("occurrence-source-invalid");
            candidates.Add(new(version.Id,Convert.ToHexStringLower(version.ContentHash),from,to,$"{candidate.Kind} · transaction {candidate.TransactionSequence}"));
        }
        var state=IncidentOccurrenceRules.State(window,knownAt,candidates.Select(x=>new IncidentOccurrenceSlice(x.VersionId,x.SourceHash,x.From,x.To)).ToArray());
        if(state=="resolved"&&subject is JsonElement supplied)
        {
            using var snapshot=JsonDocument.Parse(versions[candidates[0].VersionId].SnapshotJson);
            if(!IncidentSubjectRules.Ready(snapshot.RootElement,supplied))state="incomplete";
        }
        return new(policyId,knownAt,state,window,candidates,state=="resolved"?candidates[0].SourceHash:null);
    }
    public static IReadOnlyList<(PolicyTemporalCandidate Candidate,DateTimeOffset From,DateTimeOffset To)> Intervals(IEnumerable<PolicyTemporalCandidate> history,Guid policyId,IncidentOccurrenceWindow window,DateTimeOffset knownAt)
    {
        var retained=history.ToArray();
        var intervals=new List<(PolicyTemporalCandidate Candidate,DateTimeOffset From,DateTimeOffset To)>();
        void Add(DateTimeOffset from,DateTimeOffset to)
        {
            var selected=PolicyTemporalSelector.Select(retained,policyId,from,knownAt);
            if(selected?.State=="active")intervals.Add((selected.Candidate,from,to));
        }
        if(window.IsExact)Add(window.From,window.To);
        else
        {
            var cuts=retained.SelectMany(x=>new[]{x.StartsAt,x.EndsAt,x.EffectiveAt}).Where(x=>x>window.From&&x<window.To).Append(window.From).Append(window.To).Distinct().Order().ToArray();
            for(var index=0;index<cuts.Length-1;index++)Add(cuts[index],cuts[index+1]);
        }
        return intervals;
    }
}
