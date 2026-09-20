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
    [Theory]
    [InlineData("normal")]
    [InlineData("outside-appetite")]
    [InlineData("binder-limit")]
    [InlineData("lower-grant")]
    public async Task RealSqlCommercialReferralProofSubjectsAndAuthorityBeforeReplay(string scenario)
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await CommercialUnderwritingSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var f = await Fixture(db, 3, "commercial-combined");
            var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var clock = new RatingClock(); var quotes = new QuoteService(factory, clock); var rating = new QuoteRatingService(factory, clock);
            var relationship = await db.Set<Quote>().Where(x => x.Id == f.Quote).Select(x => x.RelationshipId).SingleAsync();
            using var stream = typeof(UnderwritingRuntimeTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
            var proposal = JsonNode.Parse(stream)!; proposal["termIntent"]!["localStartDate"] = "2026-09-17";
            if (scenario == "outside-appetite") proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.755d136885fb")!["value"] = true;
            if (scenario == "binder-limit") proposal["risk"]!["locations"]![0]!["buildings"] = "2500000.01";
            var created = await quotes.CreateAsync(f.Actor, relationship, f.ProductVersion, proposal.ToJsonString(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await quotes.GetAsync(f.Actor, created.ResourceId);
            await rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Commercial review fixture", Guid.NewGuid().ToString(), Guid.NewGuid());
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
            var leases = new SqlJobLeases(factory, clock); var worker = new QuoteRatingWorker(factory, clock);
            var lease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var senior = await db.Set<StaffUser>().SingleAsync(x => x.Email == "senior-underwriter@cover.example");
            var actor = new ActorContext(senior.Id, senior.TeamId, null, new HashSet<string> { "senior-underwriter" });
            var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
            var authority = await db.Set<AuthorityVersion>().SingleAsync(x => x.Id == cycle.AuthorityVersionId);
            if (scenario == "lower-grant")
            {
                var definition = JsonNode.Parse(authority.DefinitionJson)!; definition["version"] = "commercial-test-lower"; definition["limits"]!["annualPremium"] = "1.00";
                authority = new AuthorityVersion { ProductId = authority.ProductId, ProductVersionId = authority.ProductVersionId, BinderVersionId = authority.BinderVersionId,
                    Version = "commercial-test-lower", EffectiveFrom = authority.EffectiveFrom, EffectiveTo = authority.EffectiveTo, DefinitionJson = definition.ToJsonString(), CreatedBy = admin.Id };
                db.Add(authority); await db.SaveChangesAsync();
            }
            var grant = new UserAuthorityGrant { UserId = senior.Id, AuthorityVersionId = authority.Id, GrantedBy = admin.Id, CreatedBy = admin.Id,
                CreatedAt = Now, EffectiveFrom = authority.EffectiveFrom, EffectiveTo = authority.EffectiveTo, Reason = "Explicit test-only commercial authority" };
            db.Add(grant); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var evidence = new UnderwritingEvidenceService(factory, clock); var decisions = new QuoteReferralService(factory, clock);
            var requirements = await evidence.RequirementsAsync(actor, created.ResourceId);
            Assert.All(requirements, x => Assert.StartsWith("cc-", x.Code));
            Assert.Equal(2, requirements.Count(x => x.Code == "cc-location-proof"));
            var purpose = requirements.First(x => x.Code == "cc-location-proof");
            var current = await quotes.GetAsync(actor, created.ResourceId);
            var upload = await evidence.UploadAsync(actor, created.ResourceId, current.Quote.RowVersion, "fictional-proof.txt", "text/plain", "Fictional proof"u8.ToArray(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var staleVersion = current.Quote.RowVersion;
            current = await quotes.GetAsync(actor, created.ResourceId);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => evidence.AttachAsync(actor, created.ResourceId, cycle.Id, staleVersion, upload.ResourceId,
                purpose.Code, purpose.RiskItemId, null, null, purpose.InputFingerprint, "Stale attachment", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            if (scenario == "normal")
            {
                var rejected = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT QuoteEvidenceFile (Id,QuoteId,FileName,ContentType,Content,ByteLength,Sha256,ScreeningState,ScreeningMethod,CreatedAt,CreatedBy) SELECT NEWID(),QuoteId,FileName,ContentType,Content,ByteLength,Sha256,N'pending',ScreeningMethod,CreatedAt,CreatedBy FROM QuoteEvidenceFile WHERE Id={upload.ResourceId}"));
                Assert.Contains("CK_QuoteEvidenceFile_Screening", rejected.Message);
            }
            var foreign = await Assert.ThrowsAsync<QuoteOperationException>(() => evidence.AttachAsync(actor, created.ResourceId, cycle.Id, current.Quote.RowVersion, upload.ResourceId,
                purpose.Code, Guid.NewGuid(), null, null, purpose.InputFingerprint, "Wrong location", Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Equal("evidence-purpose-inapplicable", foreign.Code);
            var attached = await evidence.AttachAsync(actor, created.ResourceId, cycle.Id, current.Quote.RowVersion, upload.ResourceId,
                purpose.Code, purpose.RiskItemId, null, null, purpose.InputFingerprint, "Correct location", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.False((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.Code == purpose.Code && x.RiskItemId == purpose.RiskItemId).Satisfied);
            current = await quotes.GetAsync(actor, created.ResourceId);
            var association = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            var reviewKey = Guid.NewGuid().ToString(); var reviewVersion = current.Quote.RowVersion;
            var reviewed = await evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, association.Id, reviewVersion, association.RowVersion, "accepted", purpose.InputFingerprint, "Review current location", reviewKey, Guid.NewGuid());
            Assert.Equal(200, reviewed.Status);
            Assert.True((await evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, association.Id, reviewVersion, association.RowVersion, "accepted", purpose.InputFingerprint, "Review current location", reviewKey, Guid.NewGuid())).Replayed);
            Assert.True((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.Code == purpose.Code && x.RiskItemId == purpose.RiskItemId).Satisfied);
            if (scenario == "normal")
            {
                var sourceCodes = new[] { "cc-electrical-proof", "cc-alarm-proof", "cc-structural-proof", "cc-claims-experience-proof", "cc-health-safety-proof" };
                foreach (var source in requirements.Where(x => sourceCodes.Contains(x.Code)).OrderBy(x => Array.IndexOf(sourceCodes, x.Code)))
                {
                    current = await quotes.GetAsync(actor, created.ResourceId);
                    var proof = await evidence.AttachAsync(actor, created.ResourceId, cycle.Id, current.Quote.RowVersion, upload.ResourceId,
                        source.Code, source.RiskItemId, null, null, source.InputFingerprint, "Separate source document purpose", Guid.NewGuid().ToString(), Guid.NewGuid());
                    var proofRow = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == proof.ResourceId);
                    current = await quotes.GetAsync(actor, created.ResourceId);
                    await evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, proofRow.Id, current.Quote.RowVersion, proofRow.RowVersion, "accepted", source.InputFingerprint,
                        "Independent source document review", Guid.NewGuid().ToString(), Guid.NewGuid());
                    var checkedProofs = await evidence.RequirementsAsync(actor, created.ResourceId);
                    Assert.True(checkedProofs.Single(x => x.Code == source.Code && x.RiskItemId == source.RiskItemId).Satisfied);
                    if (source.Code == "cc-electrical-proof") Assert.False(checkedProofs.Single(x => x.Code == "cc-alarm-proof" && x.RiskItemId == source.RiskItemId).Satisfied);
                }
            }
            var referral = await db.Set<QuoteReferral>().AsNoTracking().FirstAsync(x => x.CycleId == cycle.Id);
            current = await quotes.GetAsync(actor, created.ResourceId);
            var condition = JsonSerializer.SerializeToElement(new { code = "provide-cc-location-proof", riskItemId = purpose.RiskItemId });
            var query = await decisions.DecideAsync(actor, created.ResourceId, cycle.Id, current.Quote.RowVersion,
                [new(referral.Id, referral.RowVersion, "query", "Ask about the saved location", [condition], "Please supply the location survey")], Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal(200, query.Status);
            current = await quotes.GetAsync(actor, created.ResourceId);
            referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id);
            var approvalVersion = current.Quote.RowVersion; var referralVersion = referral.RowVersion;
            var approval = () => decisions.DecideAsync(actor, created.ResourceId, cycle.Id, approvalVersion,
                [new(referral.Id, referralVersion, "approve-with-conditions", "Review within current authority", [condition])], Guid.NewGuid().ToString(), Guid.NewGuid());
            if (scenario == "normal") Assert.Equal(200, (await approval()).Status);
            else Assert.Equal("underwriting-dimension-authority-required", (await Assert.ThrowsAsync<QuoteOperationException>(approval)).Code);
            if (scenario == "normal")
            {
                referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id);
                var conditionRow = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.DecisionId == referral.LatestDecisionId);
                var conditionPurpose = (await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.ConditionId == conditionRow.Id);
                current = await quotes.GetAsync(actor, created.ResourceId);
                var linked = await evidence.AttachAsync(actor, created.ResourceId, cycle.Id, current.Quote.RowVersion, upload.ResourceId, conditionPurpose.Code,
                    conditionPurpose.RiskItemId, conditionRow.Id, null, conditionPurpose.InputFingerprint, "Proof for this exact condition", Guid.NewGuid().ToString(), Guid.NewGuid());
                current = await quotes.GetAsync(actor, created.ResourceId);
                Assert.Equal("condition-proof-required", (await Assert.ThrowsAsync<QuoteOperationException>(() => decisions.ResolveAsync(actor, created.ResourceId, referral.Id, cycle.Id, conditionRow.Id,
                    current.Quote.RowVersion, conditionRow.RowVersion, linked.ResourceId, "satisfied", "Unreviewed proof", Guid.NewGuid().ToString(), Guid.NewGuid()))).Code);
                var linkedRow = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == linked.ResourceId);
                await evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, linked.ResourceId, current.Quote.RowVersion, linkedRow.RowVersion, "accepted", conditionPurpose.InputFingerprint,
                    "Independent condition proof review", Guid.NewGuid().ToString(), Guid.NewGuid());
                current = await quotes.GetAsync(actor, created.ResourceId);
                await decisions.ResolveAsync(actor, created.ResourceId, referral.Id, cycle.Id, conditionRow.Id, current.Quote.RowVersion, conditionRow.RowVersion, linked.ResourceId,
                    "satisfied", "Current reviewed condition proof", Guid.NewGuid().ToString(), Guid.NewGuid());
                Assert.Equal("approved", await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.Id == referral.Id).Select(x => x.State).SingleAsync());
                current = await quotes.GetAsync(actor, created.ResourceId);
                linkedRow = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == linked.ResourceId);
                await evidence.WithdrawAsync(actor, created.ResourceId, cycle.Id, linkedRow.Id, current.Quote.RowVersion, linkedRow.RowVersion,
                    "Withdraw resolved condition proof", Guid.NewGuid().ToString(), Guid.NewGuid());
                Assert.Equal("conditional", await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.Id == referral.Id).Select(x => x.State).SingleAsync());
            }
            current = await quotes.GetAsync(actor, created.ResourceId);
            association = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await evidence.WithdrawAsync(actor, created.ResourceId, cycle.Id, association.Id, current.Quote.RowVersion, association.RowVersion, "Superseded proof", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.False((await evidence.RequirementsAsync(actor, created.ResourceId)).Single(x => x.Code == purpose.Code && x.RiskItemId == purpose.RiskItemId && x.ConditionId == null).Satisfied);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={Now},RevokedBy={admin.Id},RevocationReason=N'Test revocation' WHERE Id={grant.Id}");
            var revoked = await Assert.ThrowsAsync<QuoteOperationException>(() => evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, association.Id, reviewVersion, association.RowVersion, "accepted", purpose.InputFingerprint, "Review current location", reviewKey, Guid.NewGuid()));
            Assert.Equal(403, revoked.Status);
            if (scenario == "normal")
            {
                current = await quotes.GetAsync(f.Actor, created.ResourceId);
                await new QuoteUnderwritingLifecycle(factory, clock).ReturnToDraftAsync(f.Actor, created.ResourceId, cycle.Id, current.Quote.RowVersion,
                    "Revise the location schedule", Guid.NewGuid().ToString(), Guid.NewGuid());
                current = await quotes.GetAsync(f.Actor, created.ResourceId);
                proposal["risk"]!["locations"]!.AsArray().RemoveAt(0);
                await quotes.SaveAsync(f.Actor, created.ResourceId, current.Quote.RowVersion, proposal.ToJsonString(), "Remove a location", Guid.NewGuid().ToString(), Guid.NewGuid());
                var changed = await Assert.ThrowsAsync<QuoteOperationException>(() => evidence.ReviewAsync(actor, created.ResourceId, cycle.Id, association.Id, reviewVersion,
                    association.RowVersion, "accepted", purpose.InputFingerprint, "Review current location", reviewKey, Guid.NewGuid()));
                Assert.Equal("underwriting-cycle-stale", changed.Code);
                Assert.All(await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id).ToArrayAsync(), x => Assert.Equal("superseded", x.State));
            }
        });
    }
}
