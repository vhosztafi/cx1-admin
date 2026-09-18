using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks", "success")]
    [InlineData("motor-trade-combined", "success")]
    [InlineData("motor-trade-road-risks", "fail-once")]
    [InlineData("motor-trade-road-risks", "timeout-after-success")]
    [InlineData("motor-trade-road-risks", "reject")]
    [InlineData("motor-trade-road-risks", "revoke-before-apply")]
    [InlineData("motor-trade-road-risks", "rotate-rule")]
    [InlineData("motor-trade-road-risks", "temporary-cover")]
    [InlineData("motor-trade-road-risks", "operator-retry")]
    [InlineData("motor-trade-road-risks", "evidence-storage")]
    [InlineData("motor-trade-combined", "evidence-storage")]
    [InlineData("motor-trade-road-risks", "evidence-service")]
    [InlineData("motor-trade-combined", "evidence-service")]
    [InlineData("motor-trade-road-risks", "evidence-review")]
    [InlineData("motor-trade-combined", "evidence-review")]
    [InlineData("motor-trade-road-risks", "referral-storage")]
    [InlineData("motor-trade-combined", "referral-storage")]
    [InlineData("motor-trade-road-risks", "referral-generation")]
    [InlineData("motor-trade-combined", "referral-generation")]
    [InlineData("motor-trade-road-risks", "warranty-proof")]
    [InlineData("motor-trade-combined", "warranty-proof")]
    [InlineData("motor-trade-road-risks", "trading-proof")]
    [InlineData("motor-trade-combined", "trading-proof")]
    [InlineData("motor-trade-road-risks", "referral-service")]
    [InlineData("motor-trade-combined", "referral-service")]
    [InlineData("motor-trade-road-risks", "referral-authority")]
    [InlineData("motor-trade-combined", "referral-authority")]
    public async Task RealSqlServicingRatingRequestsPinFullScheduleAndReauthorizeReplay(string product, string scenario)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            await using (var seed = await db.Database.BeginTransactionAsync()) { await ServicingRatingSeed.SeedAsync(db); await seed.CommitAsync(); }
            if (scenario == "rotate-rule")
            {
                var original = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
                var rule = await db.Set<RatingRuleVersion>().AsNoTracking().SingleAsync(x => x.Id == original.RatingRuleVersionId);
                var definition = JsonNode.Parse(rule.DefinitionJson)!; definition["effectiveFrom"] = f.Clock.GetUtcNow();
                definition["version"] = "servicing-current-rule";
                var replacement = new RatingRuleVersion { ProductId = rule.ProductId, Version = "servicing-current-rule", State = "published",
                    EffectiveFrom = f.Clock.GetUtcNow(), EffectiveTo = rule.EffectiveTo, DefinitionJson = definition.ToJsonString() };
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State='retired' WHERE Id={rule.Id}");
                db.Add(replacement); await db.SaveChangesAsync();
                var runtime = await db.Set<SettingVersion>().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstAsync();
                var configuration = JsonNode.Parse(runtime.Values)!;
                configuration["products"]!.AsArray().Single(x => x!["productVersionId"]!.GetValue<Guid>() == original.ProductVersionId)!["ratingRuleVersionId"] = replacement.Id;
                db.Add(new SettingVersion { Scope = runtime.Scope, Version = runtime.Version + 1, EffectiveFrom = f.Clock.GetUtcNow(), Values = configuration.ToJsonString() });
                await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            }
            if (scenario is "fail-once" or "timeout-after-success" or "reject")
            {
                var runtime = await db.Set<SettingVersion>().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstAsync();
                var configuration = JsonNode.Parse(runtime.Values)!;
                configuration["scenarioVersionId"] = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-rating/" + scenario).Select(x => x.Id).SingleAsync();
                db.Add(new SettingVersion { Scope = runtime.Scope, Version = runtime.Version + 1, EffectiveFrom = f.Clock.GetUtcNow(), Values = configuration.ToJsonString() });
                await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            }
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var sourceQuote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId);
            var drafts = new ServicingDraftService(f.Factory, f.Clock); var ratings = new ServicingRatingService(f.Factory, f.Clock);
            var readModel = new ServicingRatingReadModel(f.Factory, f.Clock);
            static byte[] Version(string etag) => Convert.FromBase64String(etag.Trim('"'));
            static string Key() => Guid.NewGuid().ToString();
            var listed = await drafts.ListAsync(f.Servicing, issued.TermId);
            var created = await drafts.CreateAsync(f.Servicing, issued.TermId, Version(listed.Etag),
                new("adjustment", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional rated servicing request"), Key(), Guid.NewGuid());
            var acquired = await drafts.LeaseAsync(f.Servicing, created.ResourceId, Version(created.Etag!), "acquire", null, null, Key(), Guid.NewGuid());
            var body = JsonNode.Parse(acquired.Body)!; var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>();
            var draftId = created.ResourceId; var proposal = body["proposal"]!.DeepClone();
            var emptyView = await readModel.ReadAsync(f.Servicing, draftId);
            Assert.Empty(emptyView.Items); Assert.Null(emptyView.Current); Assert.Null(emptyView.NextBeforeSequence);
            Assert.Equal(422, (await Assert.ThrowsAsync<QuoteOperationException>(() => ratings.RateAsync(f.Servicing, draftId,
                body["revisionId"]!.GetValue<Guid>(), Version(acquired.Etag!), fence, "Rate the empty fictional draft", Key(), Guid.NewGuid()))).Status);
            var snapshot = JsonNode.Parse(issued.SnapshotJson)!; var first = Guid.NewGuid(); var second = Guid.NewGuid();
            proposal["dateBasis"] = "per-cover-change";
            proposal["changes"] = JsonSerializer.SerializeToNode(new object[] {
                new { changeId = first, riskItemId = snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<Guid>(), kind = "driver", operation = "update", payload = new { fullName = "Fictional Correction", firstName = "Fictional", surname = "Correction" } },
                new { changeId = second, riskItemId = issued.PolicyId, kind = "cover", operation = "update", payload = new { },
                    effectiveIntent = new { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" } }
            });
            if (scenario is "temporary-cover" or "referral-generation" or "referral-authority")
            {
                var originalSections = snapshot["cover"]!["requestedSections"]!.DeepClone();
                var temporarySections = originalSections.DeepClone();
                var tools = temporarySections.AsArray().Single(x => x!["code"]!.GetValue<string>() == "tools-equipment")!;
                tools["selected"] = true; tools["limit"] = scenario is "referral-generation" or "referral-authority" ? "10000.00" : "1000.00"; tools["excess"] = "100.00";
                proposal["changes"] = JsonSerializer.SerializeToNode(new object[] {
                    new { changeId = first, riskItemId = issued.PolicyId, kind = "cover", operation = "update", payload = new { requestedSections = temporarySections } },
                    new { changeId = second, riskItemId = issued.PolicyId, kind = "cover", operation = "update", payload = new { requestedSections = originalSections },
                        effectiveIntent = new { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" } }
                });
            }
            if (scenario == "trading-proof")
            {
                proposal["changes"]![0] = JsonSerializer.SerializeToNode(new {
                    changeId = first, riskItemId = issued.PolicyId, kind = "business", operation = "update",
                    payload = new { startedOn = "2025-01-01" }
                });
            }
            var saved = await drafts.SaveAsync(f.Servicing, draftId, Version(acquired.Etag!), fence, proposal.ToJsonString(), Key(), Guid.NewGuid());
            var revisionId = JsonSerializer.Deserialize<JsonElement>(saved.Body).GetProperty("revisionId").GetGuid();
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => ratings.RateAsync(f.Servicing, draftId, revisionId, Version(saved.Etag!), Guid.NewGuid(), "Wrong fictional lease must fail", Key(), Guid.NewGuid()))).Status);
            var editor = JsonSerializer.Deserialize<JsonElement>((await drafts.ReadEditorAsync(f.Servicing, draftId)).Body);
            Assert.Empty(editor.GetProperty("assessment").GetProperty("readinessIssues").EnumerateArray());
            if (scenario is "temporary-cover" or "referral-generation" or "referral-authority") Assert.Empty(editor.GetProperty("assessment").GetProperty("changes").EnumerateArray());
            var key = Key(); var reason = "Rate exact fictional cumulative proposal";
            var requested = await ratings.RateAsync(f.Servicing, draftId, revisionId, Version(saved.Etag!), fence, reason, key, Guid.NewGuid());
            Assert.Equal(202, requested.Status);
            Assert.True((await ratings.RateAsync(f.Servicing, draftId, revisionId, Version(saved.Etag!), fence, reason, key, Guid.NewGuid())).Replayed);
            var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.DraftId == draftId);
            var input = ServicingRatingInput.Read(cycle.InputJson, cycle.InputHash);
            Assert.Equal(2, input.Slices.Count); Assert.Equal([first], input.Slices[0].ChangeIds);
            Assert.Equal(2, input.Slices[1].ChangeIds.Count); Assert.Contains(first, input.Slices[1].ChangeIds); Assert.Contains(second, input.Slices[1].ChangeIds);
            Assert.Equal(15m, input.Fee); Assert.Equal(cycle.CreatedAt, input.RequestedAt);
            Assert.Equal(revisionId, input.RevisionId); Assert.Equal(Convert.ToHexStringLower(issued.ContentHash), input.BaseContentHash);
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == cycle.WorkId);
            Assert.Equal("pending", work.State); Assert.Equal("servicing-rating", work.Kind); Assert.Equal(cycle.Id, work.SubjectRecordId);
            var jobs = new SqlJobLeases(f.Factory, f.Clock); var worker = new ServicingRatingWorker(f.Factory, f.Clock);
            var pendingView = await readModel.ReadAsync(f.Servicing, draftId);
            Assert.Equal(cycle.Id, pendingView.CurrentCycleId);
            Assert.Equal("rating-pending", Assert.Single(pendingView.Items).State);
            Assert.False(pendingView.Items[0].Applicable); Assert.Null(pendingView.Items[0].Result);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => readModel.ReadAsync(f.Servicing, draftId, beforeSequence: 0))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => readModel.ReadAsync(f.Servicing, draftId, pageSize: 51))).Status);
            if (scenario == "operator-retry")
            {
                for (var attempt = 1; attempt <= 6; attempt++)
                {
                    var failedLease = Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating", work.Id));
                    Assert.Equal(attempt, failedLease.Attempt);
                    Assert.True(await jobs.FailAsync(failedLease, JobFailure.ProviderUnavailable));
                    var failed = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == work.Id);
                    if (attempt < 6) f.Clock.Current = failed.NextAttemptAt.AddSeconds(1);
                }
                var failedWork = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == work.Id);
                Assert.Equal("failed", failedWork.State);
                Assert.Equal("failed", (await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycle.Id)).State);
                var recovery = new ServicingRatingJobs(f.Factory, f.Clock); var retryKey = Key();
                const string retryReason = "Recover fictional servicing rating";
                Assert.False((await recovery.ReadAsync(f.Servicing, work.Id)).RetryAllowed);
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => recovery.RetryAsync(f.Servicing, work.Id,
                    failedWork.RowVersion, retryReason, retryKey, Guid.NewGuid()))).Status);
                var adminRole = await db.Set<Role>().SingleAsync(x => x.Code == "system-admin");
                var link = new UserRole { UserId = f.Servicing.UserId, RoleId = adminRole.Id }; db.Add(link); await db.SaveChangesAsync();
                var recoveryActor = f.Servicing with { Roles = new HashSet<string> { "servicing", "system-admin" } };
                Assert.True((await recovery.ReadAsync(recoveryActor, work.Id)).RetryAllowed);
                Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => recovery.RetryAsync(recoveryActor, work.Id,
                    work.RowVersion, retryReason, Key(), Guid.NewGuid()))).Status);
                await VerifyServicingRatingHttpRetry(db, f.Clock, password, work.Id, failedWork.RowVersion, retryReason, retryKey);
                var recovered = await recovery.RetryAsync(recoveryActor, work.Id, failedWork.RowVersion, retryReason, retryKey, Guid.NewGuid());
                Assert.Equal(202, recovered.Status);
                Assert.False((await recovery.ReadAsync(recoveryActor, work.Id)).RetryAllowed);
                Assert.True((await recovery.RetryAsync(recoveryActor, work.Id, failedWork.RowVersion, retryReason, retryKey, Guid.NewGuid())).Replayed);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE Id={link.Id}"); db.ChangeTracker.Clear();
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => recovery.RetryAsync(recoveryActor, work.Id,
                    failedWork.RowVersion, retryReason, retryKey, Guid.NewGuid()))).Status);
                var recoveredLease = Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating", work.Id));
                Assert.Equal(7, recoveredLease.Attempt);
                Assert.True(await worker.ApplyAsync(recoveredLease, await worker.ExecuteProviderAsync(recoveredLease)));
                Assert.Equal("rated", (await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycle.Id)).State);
                Assert.Equal(7, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == work.Id));
                Assert.Single(await db.Set<ServicingRatingResult>().Where(x => x.CycleId == cycle.Id).ToArrayAsync());
                db.Add(new UserRole { UserId = f.Servicing.UserId, RoleId = adminRole.Id });
                var previousSetting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Scope == ServicingRatingSeed.Scope);
                var changedSetting = JsonNode.Parse(previousSetting.Values)!; changedSetting["adjustmentFee"] = "16.00";
                db.Add(new SettingVersion { Scope = previousSetting.Scope, Version = previousSetting.Version + 1,
                    EffectiveFrom = f.Clock.GetUtcNow(), Values = changedSetting.ToJsonString() });
                await db.SaveChangesAsync();
                Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => recovery.RetryAsync(recoveryActor, work.Id,
                    failedWork.RowVersion, retryReason, retryKey, Guid.NewGuid()))).Status);
                var staleView = await readModel.ReadAsync(recoveryActor, draftId);
                Assert.Equal("stale", staleView.Current!.State); Assert.False(staleView.Current.Applicable);
                Assert.NotNull(staleView.Current.Result); Assert.Contains("servicing-rating-cycle-stale", staleView.Blockers);
                return;
            }
            async Task<(JobLease Lease, ServicingRatingOutcome Outcome)> Execute(Guid workId)
            {
                var lease = Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating", workId));
                if (scenario is "fail-once" or "timeout-after-success")
                {
                    var failure = await Assert.ThrowsAsync<QuoteRatingProviderException>(() => worker.ExecuteProviderAsync(lease));
                    Assert.Equal(scenario == "fail-once" ? JobFailure.ProviderUnavailable : JobFailure.ProviderTimeout, failure.Failure);
                    Assert.True(await jobs.FailAsync(lease, failure.Failure));
                    f.Clock.Current = (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == workId)).NextAttemptAt.AddSeconds(1);
                    lease = Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating", workId));
                    Assert.Equal(2, lease.Attempt);
                }
                return (lease, await worker.ExecuteProviderAsync(lease));
            }
            var (firstLease, firstOutcome) = await Execute(work.Id);
            Assert.Equal(JsonSerializer.Serialize(firstOutcome), JsonSerializer.Serialize(await worker.ExecuteProviderAsync(firstLease)));
            Assert.False(await worker.ApplyAsync(firstLease with { Token = Guid.NewGuid() }, firstOutcome));
            Assert.Empty(await db.Set<ServicingRatingResult>().Where(x => x.DraftId == draftId).ToArrayAsync());
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => ratings.RateAsync(f.Servicing, draftId, revisionId, Version(saved.Etag!), fence, reason, Key(), Guid.NewGuid()))).Status);
            var rerated = await ratings.RateAsync(f.Servicing, draftId, revisionId, Version(requested.Etag!), fence, "Request a fresh fictional rating", Key(), Guid.NewGuid());
            Assert.Equal("superseded", (await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycle.Id)).State);
            Assert.NotEqual(cycle.Id, rerated.ResourceId); Assert.Equal(2, await db.Set<ServicingCycle>().CountAsync(x => x.DraftId == draftId));
            Assert.True(await worker.ApplyAsync(firstLease, firstOutcome));
            Assert.False(await worker.ApplyAsync(firstLease, firstOutcome));
            Assert.Null((await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycle.Id)).CurrentRatingId);
            var activeCycle = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == rerated.ResourceId);
            var (secondLease, secondOutcome) = await Execute(activeCycle.WorkId);
            Assert.Equal(JobFailure.ProviderConflict, (await Assert.ThrowsAsync<QuoteRatingProviderException>(() => worker.ApplyAsync(secondLease,
                secondOutcome with { ExpiresAt = secondOutcome.ExpiresAt.AddDays(1) }))).Failure);
            var roles = await db.Set<UserRole>().AsNoTracking().Where(x => x.UserId == f.Servicing.UserId).ToArrayAsync();
            if (scenario == "revoke-before-apply") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
            Assert.True(await worker.ApplyAsync(secondLease, secondOutcome));
            var applied = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == activeCycle.Id);
            Assert.Equal(scenario is "reject" or "revoke-before-apply" ? "failed" : "rated", applied.State);
            if (scenario is "reject" or "revoke-before-apply") Assert.Null(applied.CurrentRatingId); else Assert.NotNull(applied.CurrentRatingId);
            if (scenario == "revoke-before-apply") { db.ChangeTracker.Clear(); db.AddRange(roles); await db.SaveChangesAsync(); }
            Assert.Equal(2, await db.Set<ServicingRatingResult>().CountAsync(x => x.DraftId == draftId));
            Assert.Equal(scenario == "reject" ? 0m : 15m, (await db.Set<ServicingRatingResult>().SingleAsync(x => x.CycleId == applied.Id)).Fee);
            if (scenario == "referral-generation")
            {
                Assert.Empty(await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync());
                var generated=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==applied.Id).ToArrayAsync();
                var toolsReferral=Assert.Single(generated,x=>x.RuleCode=="cover-tools-equipment");
                Assert.Equal("open",toolsReferral.State);Assert.Equal(applied.CurrentRatingId,toolsReferral.RatingId);Assert.Equal(revisionId,toolsReferral.RevisionId);
                using var required=JsonDocument.Parse(toolsReferral.RequiredAuthorityJson);var triggers=required.RootElement.GetProperty("triggers").EnumerateArray().ToArray();
                Assert.Equal(2,triggers.Length);Assert.Contains(triggers,x=>x.GetProperty("source").GetString()=="binder");Assert.Contains(triggers,x=>x.GetProperty("source").GetString()=="authority");
                Assert.All(triggers,x=>{Assert.Equal(input.Slices[0].EffectiveAt,x.GetProperty("effectiveAt").GetDateTimeOffset());Assert.Equal(10000m,x.GetProperty("requirement").GetProperty("requestedAmount").GetDecimal());});
                Assert.False(await worker.ApplyAsync(secondLease,secondOutcome));
                Assert.Equal(generated.Length,await db.Set<ServicingReferral>().CountAsync(x=>x.CycleId==applied.Id));
            }
            if (scenario is "reject" or "revoke-before-apply") Assert.Empty(await db.Set<ServicingReferral>().Where(x=>x.DraftId==draftId).ToArrayAsync());
            var ratedView = await readModel.ReadAsync(f.Servicing, draftId, pageSize: 1);
            if (scenario is "referral-service" or "referral-authority" or "trading-proof" or "warranty-proof")
            {
                if(scenario=="referral-service") await VerifyServicingReferralService(db,f,applied,ratedView.DraftEtag);
                else if(scenario=="warranty-proof") await VerifyServicingWarrantyProof(db,f,applied,ratedView.DraftEtag);
                else if(scenario=="trading-proof") await VerifyServicingTradingProof(db,f,applied,ratedView.DraftEtag);
                else await VerifyServicingReferralAuthority(db,f,applied,ratedView.DraftEtag);
                Assert.Equal(issued.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);return;
            }
            var currentView = Assert.Single(ratedView.Items);
            Assert.Equal(applied.Id, currentView.Id);
            Assert.Equal(scenario is not ("reject" or "revoke-before-apply"), currentView.Applicable);
            Assert.Equal(scenario == "reject" ? "0.00" : "15.00", currentView.Result!.Fee);
            Assert.Equal("GBP", currentView.Result.Currency); Assert.True(currentView.Result.DetailsAvailable);
            Assert.Equal(scenario == "reject" ? 0 : 2, currentView.Result.Slices.Count);
            if (scenario != "reject") { Assert.Contains(first, currentView.Result.ChangeIds); Assert.Contains(second, currentView.Result.ChangeIds); }
            Assert.Equal(2, ratedView.NextBeforeSequence);
            var older = await readModel.ReadAsync(f.Servicing, draftId, ratedView.NextBeforeSequence, 1);
            Assert.Equal(cycle.Id, Assert.Single(older.Items).Id); Assert.Null(older.NextBeforeSequence);
            Assert.False(older.Items[0].Applicable); Assert.Equal("superseded", older.Items[0].State);
            Assert.Equal(applied.Id, older.Current!.Id);
            if (scenario is "evidence-storage" or "evidence-service" or "evidence-review" or "referral-storage")
            {
                var sibling = await drafts.CreateAsync(f.Servicing, issued.TermId, Version((await drafts.ListAsync(f.Servicing, issued.TermId)).Etag),
                    new("cancellation", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional separate proof owner"), Key(), Guid.NewGuid());
                if (scenario == "evidence-storage") await VerifyServicingEvidenceAssociationStorage(db,applied,sibling.ResourceId,f.Servicing.UserId,f.Underwriter.UserId,f.Clock.GetUtcNow());
                else if (scenario == "referral-storage") await VerifyServicingReferralStorage(db,f,applied);
                else if (scenario == "evidence-review") await VerifyServicingEvidenceReview(db,f,applied,sibling.ResourceId,fence,ratedView.DraftEtag);
                else await VerifyServicingEvidenceFilesService(db,f,applied,sibling.ResourceId,fence,ratedView.DraftEtag);
                Assert.Equal(issued.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);return;
            }
            var beforeExpiry = f.Clock.Current;
            f.Clock.Current = secondOutcome.ExpiresAt;
            var expired = await readModel.ReadAsync(f.Servicing, draftId);
            Assert.False(expired.Items[0].Applicable);
            if (scenario is not ("reject" or "revoke-before-apply")) Assert.Equal("expired", expired.Items[0].State);
            f.Clock.Current = beforeExpiry;
            var revised = await drafts.SaveAsync(f.Servicing, draftId, Version(rerated.Etag!), fence, proposal.ToJsonString(), Key(), Guid.NewGuid());
            Assert.Null((await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x => x.Id == draftId)).CurrentCycleId);
            Assert.Equal("superseded", (await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == rerated.ResourceId)).State);
            if (scenario == "referral-generation")
            {
                var retainedReferrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==applied.Id).ToArrayAsync();
                Assert.NotEmpty(retainedReferrals);Assert.All(retainedReferrals,x=>Assert.Equal("superseded",x.State));
            }
            var revisedId = JsonSerializer.Deserialize<JsonElement>(revised.Body).GetProperty("revisionId").GetGuid();
            var finalRating = await ratings.RateAsync(f.Servicing, draftId, revisedId, Version(revised.Etag!), fence, reason, Key(), Guid.NewGuid());
            var finalCycle = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == finalRating.ResourceId);
            var (lastLease, lastOutcome) = await Execute(finalCycle.WorkId);
            await drafts.AbandonAsync(f.Servicing, draftId, Version(finalRating.Etag!), fence, "Abandon fictional rated adjustment", Key(), Guid.NewGuid());
            Assert.True(await worker.ApplyAsync(lastLease, lastOutcome));
            Assert.Null((await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == finalCycle.Id)).CurrentRatingId);
            Assert.Null((await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x => x.Id == draftId)).CurrentCycleId);
            Assert.Equal("superseded", (await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == finalRating.ResourceId)).State);
            Assert.Equal(3, await db.Set<ServicingCycle>().CountAsync(x => x.DraftId == draftId));
            Assert.Equal(3, await db.Set<OutboxWork>().CountAsync(x => x.Kind == "servicing-rating"));
            Assert.Equal(3, await db.Set<ServicingRatingResult>().CountAsync(x => x.DraftId == draftId));
            Assert.Equal(3, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == "servicing-rating"));
            var abandonedView = await readModel.ReadAsync(f.Servicing, draftId);
            Assert.Null(abandonedView.CurrentCycleId); Assert.Equal(3, abandonedView.Items.Count);
            Assert.All(abandonedView.Items, item => Assert.False(item.Applicable));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => readModel.ReadAsync(f.Servicing, draftId))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => ratings.RateAsync(f.Servicing, draftId, revisionId, Version(saved.Etag!), fence, reason, key, Guid.NewGuid()))).Status);
            var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(); Assert.Equal(issued.SnapshotJson, retained.SnapshotJson); Assert.Equal(issued.ContentHash, retained.ContentHash);
            var retainedQuote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId);
            Assert.Equal("bound", retainedQuote.State); Assert.Equal(sourceQuote.RowVersion, retainedQuote.RowVersion);
        });
    }
}
