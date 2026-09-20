using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialTermsCarrierConditionsAndIndependentAcceptance() => CommercialTermsScenario();

    private async Task CommercialTermsScenario(Func<BackOfficeDbContext, UnderwritingCycle, Guid, Guid, DateTimeOffset, Task>? afterAccepted = null, bool stopAfterAccepted = false, Action<JsonNode>? configureProposal = null, BackOfficeDbContext? existingDb = null, Func<BackOfficeDbContext, string, Task>? inspectAccepted = null)
    {
        async Task Run(BackOfficeDbContext db, string password)
        {
            if (existingDb is null)
            {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await CommercialUnderwritingSeed.SeedAsync(db); await CapacitySeed.SeedAsync(db); await QuoteTermsSeed.SeedAsync(db); await transaction.CommitAsync(); }
            }
            var f = await Fixture(db, 3, "commercial-combined");
            var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var clock = new RatingClock(); var quotes = new QuoteService(factory, clock); var rating = new QuoteRatingService(factory, clock);
            var relationship = await db.Set<Quote>().Where(x => x.Id == f.Quote).Select(x => x.RelationshipId).SingleAsync();
            using var stream = typeof(UnderwritingRuntimeTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
            var proposal = JsonNode.Parse(stream)!; proposal["termIntent"]!["localStartDate"] = "2026-09-17";
            proposal["risk"]!["locations"]![0]!["buildings"] = "2500000.01";
            configureProposal?.Invoke(proposal);
            var created = await quotes.CreateAsync(f.Actor, relationship, f.ProductVersion, proposal.ToJsonString(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await quotes.GetAsync(f.Actor, created.ResourceId);
            await rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Commercial review fixture", Guid.NewGuid().ToString(), Guid.NewGuid());
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.QuoteId == created.ResourceId);
            var leases = new SqlJobLeases(factory, clock); var worker = new QuoteRatingWorker(factory, clock);
            var lease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var senior = await db.Set<StaffUser>().SingleAsync(x => x.Email == "senior-underwriter@cover.example");
            var actor = new ActorContext(senior.Id, senior.TeamId, null, new HashSet<string> { "senior-underwriter" });
            var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
            var authority = await db.Set<AuthorityVersion>().SingleAsync(x => x.Id == cycle.AuthorityVersionId);
            var grant = new UserAuthorityGrant { UserId = senior.Id, AuthorityVersionId = authority.Id, GrantedBy = admin.Id, CreatedBy = admin.Id,
                CreatedAt = Now, EffectiveFrom = authority.EffectiveFrom, EffectiveTo = authority.EffectiveTo, Reason = "Explicit test-only commercial authority" };
            if (!await db.Set<UserAuthorityGrant>().AnyAsync(x => x.UserId == senior.Id && x.AuthorityVersionId == authority.Id && x.RevokedAt == null))
            { db.Add(grant); await db.SaveChangesAsync(); }
            db.ChangeTracker.Clear();
            var evidence = new UnderwritingEvidenceService(factory, clock); var decisions = new QuoteReferralService(factory, clock);
            async Task<byte[]> Version() => (await quotes.GetAsync(actor, created.ResourceId)).Quote.RowVersion;
            string Key() => Guid.NewGuid().ToString();
            var uploaded = await evidence.UploadAsync(actor, created.ResourceId, await Version(), "fictional-cc-proof.txt", "text/plain", "Fictional CC proof"u8.ToArray(), Key(), Guid.NewGuid());
            async Task<Guid> Proof(UnderwritingProofRequirement purpose)
            {
                var attached = await evidence.AttachAsync(actor, created.ResourceId, cycle.Id, await Version(), uploaded.ResourceId, purpose.Code,
                    purpose.RiskItemId, purpose.ConditionId, purpose.TermsVersionId, purpose.InputFingerprint, "Exact current commercial proof", Key(), Guid.NewGuid(), capacitySubmissionId: purpose.CapacitySubmissionId);
                var row = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
                await evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, row.Id, await Version(), row.RowVersion, "accepted", purpose.InputFingerprint, "Independent review", Key(), Guid.NewGuid());
                return row.Id;
            }
            foreach (var purpose in await evidence.RequirementsAsync(actor, created.ResourceId)) await Proof(purpose);
            var capacity = new CapacityService(factory, clock); var capacityWorker = new CapacityWorker(factory, clock);
            var scenario = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "capacity-escalation/cc-conditional-proof");
            foreach (var referral in await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id && (x.Dimension == "single-location" || x.Dimension == "maximum-estimated-loss")).ToArrayAsync())
            {
                var provider = (await db.Set<BinderVersion>().SingleAsync(x => x.Id == cycle.BinderVersionId)).ProviderId;
                var createVersion = await Version(); var createKey = Key();
                Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => capacity.CreateAsync(actor, created.ResourceId, cycle.Id, referral.Id, new byte[8], referral.RowVersion, provider, "Exact location exception", Key(), Guid.NewGuid()))).Status);
                var escalation = await capacity.CreateAsync(actor, created.ResourceId, cycle.Id, referral.Id, createVersion, referral.RowVersion, provider, "Exact location exception", createKey, Guid.NewGuid());
                Assert.True((await capacity.CreateAsync(actor, created.ResourceId, cycle.Id, referral.Id, createVersion, referral.RowVersion, provider, "Exact location exception", createKey, Guid.NewGuid())).Replayed);
                var request = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.ResourceId);
                var submissionVersion = await Version(); var submissionKey = Key();
                await capacity.SendAsync(actor, created.ResourceId, cycle.Id, request.Id, submissionVersion, request.RowVersion, "Review this location only", [], scenario.Id, submissionKey, Guid.NewGuid());
                Assert.True((await capacity.SendAsync(actor, created.ResourceId, cycle.Id, request.Id, submissionVersion, request.RowVersion, "Review this location only", [], scenario.Id, submissionKey, Guid.NewGuid())).Replayed);
                var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.EscalationId == request.Id);
                var capacityLease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
                var response = await capacityWorker.ExecuteProviderAsync(capacityLease);
                Assert.Equal("approve-with-conditions", response.Outcome);
                Assert.True(await capacityWorker.ApplyAsync(capacityLease, response));
                Assert.False(await capacityWorker.ApplyAsync(capacityLease, response));
                Assert.Null((await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).LatestDecisionId);
            }
            var terms = new QuoteTermsService(factory, clock);
            var template = await db.Set<TemplateVersion>().SingleAsync(x => x.Code == "demo-commercial-combined-terms");
            var completed = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycle.Id);
            await Assert.ThrowsAsync<QuoteOperationException>(async () => await terms.PrepareAsync(actor, created.ResourceId, cycle.Id, completed.CurrentRatingId!.Value, template.Id, await Version(), Key(), Guid.NewGuid()));
            foreach (var purpose in (await evidence.RequirementsAsync(actor, created.ResourceId)).Where(x => x.ConditionId != null))
            {
                var proofId = await Proof(purpose);
                var condition = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.Id == purpose.ConditionId);
                await decisions.ResolveAsync(actor, created.ResourceId, condition.ReferralId, cycle.Id, condition.Id, await Version(), condition.RowVersion, proofId, "satisfied", "Resolve exact carrier proof", Key(), Guid.NewGuid());
            }
            foreach (var referral in await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id).ToArrayAsync())
                await decisions.DecideAsync(actor, created.ResourceId, cycle.Id, await Version(), [new(referral.Id, referral.RowVersion, "approve", "Independent internal decision", [], null)], Key(), Guid.NewGuid());
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => terms.PrepareAsync(actor, created.ResourceId, cycle.Id, completed.CurrentRatingId!.Value, template.Id, new byte[8], Key(), Guid.NewGuid()))).Status);
            var prepared = await terms.PrepareAsync(actor, created.ResourceId, cycle.Id, completed.CurrentRatingId!.Value, template.Id, await Version(), Key(), Guid.NewGuid());
            var retained = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == prepared.ResourceId);
            Assert.Equal("commercial-combined", JsonDocument.Parse(retained.TermsJson).RootElement.GetProperty("productCode").GetString());
            await Proof((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.Code == "signed-statement" && x.ConditionId == null));
            var acceptedProof = await Proof((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.Code == "acceptance-proof"));
            var assessment = await new QuoteUnderwritingReadModel(factory, clock).AssessmentAsync(actor, created.ResourceId);
            var acceptance = new QuoteAcceptanceService(factory, clock);
            var input = new QuoteAcceptanceInput(cycle.Id, completed.CurrentRatingId.Value, retained.Id, retained.TermsHash, (string)assessment["assuranceHash"], "Fictional commercial customer", clock.Current, "email", acceptedProof);
            Assert.Equal("quote-delivered-terms-required", (await Assert.ThrowsAsync<QuoteOperationException>(async () => await acceptance.RecordAsync(actor, created.ResourceId, await Version(), input, Key(), Guid.NewGuid()))).Code);
            var person = new Person { FullName = "Fictional commercial customer" }; db.Add(person); await db.SaveChangesAsync();
            var contact = new Contact { ClientId = cycle.ClientId, RelationshipId = cycle.RelationshipId, PersonId = person.Id,
                DeclaredFullName = person.FullName, NormalizedName = person.FullName.ToUpperInvariant(), Role = "director", Email = "commercial-customer@example.invalid",
                MarketingConsent = "{\"state\":\"not-asked\",\"email\":false,\"telephone\":false,\"source\":\"Fictional fixture\",\"recordedAt\":\"2026-09-16T12:00:00Z\"}" };
            db.Add(contact); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var sendVersion = await Version(); var sendKey = Key();
            var sent = await terms.SendAsync(actor, created.ResourceId, retained.Id, [contact.Id], sendVersion, sendKey, Guid.NewGuid());
            Assert.True((await terms.SendAsync(actor, created.ResourceId, retained.Id, [contact.Id], sendVersion, sendKey, Guid.NewGuid())).Replayed);
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var deliveryLease = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            var deliveryWorker = new QuoteDeliveryWorker(factory, clock);
            Assert.True(await deliveryWorker.ApplyAsync(deliveryLease, await deliveryWorker.ExecuteProviderAsync(deliveryLease)));
            var acceptVersion = await Version(); var acceptKey = Key();
            var accepted = await acceptance.RecordAsync(actor, created.ResourceId, acceptVersion, input, acceptKey, Guid.NewGuid());
            Assert.True((await acceptance.RecordAsync(actor, created.ResourceId, acceptVersion, input, acceptKey, Guid.NewGuid())).Replayed);
            Assert.Equal("accepted", (await quotes.GetAsync(actor, created.ResourceId)).Quote.State);
            if (afterAccepted is not null) await afterAccepted(db, cycle, accepted.ResourceId, actor.UserId, clock.Current);
            if (inspectAccepted is not null) await inspectAccepted(db, password);
            if (stopAfterAccepted) return;
            var finalAssessment = await new QuoteUnderwritingReadModel(factory, clock).AssessmentAsync(actor, created.ResourceId);
            Assert.False(JsonSerializer.SerializeToElement(finalAssessment["capabilities"], new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("canIssue").GetBoolean());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Contact SET Email=N'changed@example.invalid' WHERE Id={contact.Id}");
            Assert.Equal("quote-recipient-changed", (await Assert.ThrowsAsync<QuoteOperationException>(async () => await acceptance.RecordAsync(actor, created.ResourceId, await Version(), input, Key(), Guid.NewGuid()))).Code);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Contact SET Email=N'commercial-customer@example.invalid' WHERE Id={contact.Id}");
            var suppliedRequest = await db.Set<CapacityEscalation>().AsNoTracking().FirstAsync(x => x.CycleId == cycle.Id);
            var suppliedSubmission = await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.Id == suppliedRequest.CurrentSubmissionId);
            var suppliedReferral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == suppliedRequest.ReferralId);
            var providerProof = await Proof((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.CapacitySubmissionId == suppliedSubmission.Id));
            var extent = JsonSerializer.SerializeToElement(new { dimension = suppliedReferral.Dimension, maximumAmount = "4000000.00", riskItemId = suppliedReferral.RiskItemId });
            var supplied = new CapacityResponseInput(suppliedSubmission.Id, suppliedSubmission.ContextHash, "approve", "Fictional commercial carrier", "CC-SUPPLIED-001",
                "Actual supplied fictional letter", clock.Current, providerProof, clock.Current.AddDays(-2), cycle.EndsAt, [extent], []);
            async Task<CommandOutcome> Record(CapacityResponseInput response) => await capacity.RecordResponseAsync(actor, created.ResourceId, cycle.Id, suppliedRequest.Id,
                await Version(), (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == suppliedRequest.Id)).RowVersion, response, Key(), Guid.NewGuid());
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Record(supplied with { SubmissionHash = new string('0', 64) }))).Status);
            var foreignExtent = JsonSerializer.SerializeToElement(new { dimension = suppliedReferral.Dimension, maximumAmount = "4000000.00", riskItemId = Guid.NewGuid() });
            Assert.Equal("capacity-response-extent", (await Assert.ThrowsAsync<QuoteOperationException>(() => Record(supplied with { AuthorisedLimits = [foreignExtent] }))).Code);
            var districtExtent = JsonSerializer.SerializeToElement(new { dimension = "district-property", maximumAmount = "50000000.00" });
            Assert.Equal("capacity-response-definition", (await Assert.ThrowsAsync<QuoteOperationException>(() => Record(supplied with { AuthorisedLimits = [districtExtent] }))).Code);
            await Record(supplied with { ValidTo = clock.Current.AddSeconds(-1) });
            Assert.Contains((await Assert.ThrowsAsync<QuoteOperationException>(async () => await terms.PrepareAsync(actor, created.ResourceId, cycle.Id, completed.CurrentRatingId.Value, template.Id, await Version(), Key(), Guid.NewGuid()))).Code, new[] { "capacity-response-required", "quote-decision-authority-stale" });
            var responseVersion = await Version(); var responseKey = Key();
            var responseRequest = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == suppliedRequest.Id);
            await capacity.RecordResponseAsync(actor, created.ResourceId, cycle.Id, suppliedRequest.Id, responseVersion, responseRequest.RowVersion, supplied, responseKey, Guid.NewGuid());
            Assert.True((await capacity.RecordResponseAsync(actor, created.ResourceId, cycle.Id, suppliedRequest.Id, responseVersion, responseRequest.RowVersion, supplied, responseKey, Guid.NewGuid())).Replayed);
            Assert.Equal("quote-acceptance-context-stale", (await Assert.ThrowsAsync<QuoteOperationException>(async () => await acceptance.RecordAsync(actor, created.ResourceId, await Version(), input, Key(), Guid.NewGuid()))).Code);
            var proofRow = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == providerProof);
            await evidence.WithdrawAsync(actor, created.ResourceId, cycle.Id, providerProof, await Version(), proofRow.RowVersion, "Withdraw supplied carrier proof", Key(), Guid.NewGuid());
            Assert.Contains((await Assert.ThrowsAsync<QuoteOperationException>(async () => await terms.PrepareAsync(actor, created.ResourceId, cycle.Id, completed.CurrentRatingId.Value, template.Id, await Version(), Key(), Guid.NewGuid()))).Code, new[] { "capacity-response-required", "quote-decision-authority-stale" });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={Now},RevokedBy={admin.Id},RevocationReason=N'Revoke current carrier recording authority' WHERE Id={grant.Id}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => capacity.RecordResponseAsync(actor, created.ResourceId, cycle.Id, suppliedRequest.Id, responseVersion, responseRequest.RowVersion, supplied, responseKey, Guid.NewGuid()))).Status);
            Assert.Equal(retained.TermsJson, (await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == retained.Id)).TermsJson);
        }
        if (existingDb is not null) await Run(existingDb, string.Empty);
        else await WithDatabase(Run);
    }
}

