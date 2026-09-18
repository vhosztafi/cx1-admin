using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingCapacityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly ServicingCapacitySubject Subject = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64));
    private static readonly Guid ResponseId = Guid.NewGuid();
    private static ServicingCapacityResponse Approval => new(ResponseId, Subject, "approve", Now,
        Now.AddYears(1), [new("stock-limit", 150000m)]);
    private static ServicingCapacityExposure Exposure => new("stock-limit", null, Now.AddDays(1),
        Now.AddMonths(6), 150000m);

    [Fact]
    public void ResponseMustMatchEveryServicingOwnerAndTheLatestResponse()
    {
        Assert.True(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure, Now));
        ServicingCapacitySubject[] wrongOwners = [Subject with { DraftId = Guid.NewGuid() },
            Subject with { RevisionId = Guid.NewGuid() }, Subject with { CycleId = Guid.NewGuid() },
            Subject with { RatingId = Guid.NewGuid() }, Subject with { CaseId = Guid.NewGuid() },
            Subject with { ReferralId = Guid.NewGuid() }, Subject with { ProviderId = Guid.NewGuid() },
            Subject with { SubmissionId = Guid.NewGuid() }, Subject with { SubmissionHash = new string('b', 64) }];
        foreach (var other in wrongOwners)
            Assert.False(ServicingCapacityRules.ExtentApplies(Approval, other, ResponseId, "approved", null, Exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, Guid.NewGuid(), "approved", null, Exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject with { RatingId = Guid.Empty }, ResponseId, "approved", null, Exposure, Now));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("queued")]
    [InlineData("sent")]
    [InlineData("queried")]
    [InlineData("declined")]
    [InlineData("superseded")]
    [InlineData("failed")]
    public void RetainedResponseDoesNotGrantExtentInAnInactiveCase(string state) =>
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, state, null, Exposure, Now));

    [Fact]
    public void ExtensionMustCoverEveryDatedExposureAndExactMoney()
    {
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure with { RequestedAmount = 150000.01m }, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure with { EndsAt = Now.AddYears(1).AddTicks(1) }, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure with { StartsAt = Now.AddTicks(-1) }, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure, Now.AddYears(1)));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval, Subject, ResponseId, "approved", null, Exposure with { Dimension = "vehicle-limit" }, Now));
    }

    [Fact]
    public void DriverPermissionCannotMoveBetweenStableTargets()
    {
        var driver = Guid.NewGuid();
        var response = Approval with { Extensions = [new("driver-age", MinimumAge: 21, MaximumAge: 25)] };
        var exposure = Exposure with { Dimension = "driver-age", TargetId = driver, RequestedAmount = null, MinimumAge = 21, MaximumAge = 21 };
        Assert.True(ServicingCapacityRules.ExtentApplies(response, Subject, ResponseId, "approved", driver, exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(response, Subject, ResponseId, "approved", Guid.NewGuid(), exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(response, Subject, ResponseId, "approved", null, exposure, Now));
    }

    [Fact]
    public void ConditionalExtentIsNotAnUnconditionalResponseOrProofApproval()
    {
        var response = Approval with { Outcome = "approve-with-conditions" };
        Assert.True(ServicingCapacityRules.ExtentApplies(response, Subject, ResponseId, "conditional", null, Exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(response, Subject, ResponseId, "approved", null, Exposure, Now));
        Assert.False(ServicingCapacityRules.ExtentApplies(Approval with { Outcome = "query" }, Subject, ResponseId, "approved", null, Exposure, Now));
        // Extent is intentionally only one predicate; services must separately
        // check current grants, resolved conditions and unrelated required proof.
    }

    [Fact]
    public void WithdrawAndReopenFenceLateAndDuplicateDeliveryWithoutDeletingHistory()
    {
        Assert.True(ServicingCapacityRules.CanApplyResponse(Subject, Subject, "queued", null));
        Assert.True(ServicingCapacityRules.CanApplyResponse(Subject, Subject, "sent", null));
        var withdrawn = ServicingCapacityRules.ActionState("sent", "withdraw");
        Assert.Equal("draft", withdrawn);
        Assert.False(ServicingCapacityRules.CanApplyResponse(Subject, Subject, withdrawn, null));
        var reopened = ServicingCapacityRules.ActionState("conditional", "reopen");
        Assert.False(ServicingCapacityRules.CanApplyResponse(Subject, Subject, reopened, ResponseId));
        Assert.False(ServicingCapacityRules.CanApplyResponse(Subject, Subject, "queued", ResponseId));
        Assert.False(ServicingCapacityRules.CanApplyResponse(Subject, Subject with { SubmissionId = Guid.NewGuid() }, "queued", null));
    }

    [Fact]
    public void RoutingDoesNotChangeDecisionStateAndInvalidTransitionsFail()
    {
        Assert.Equal("queried", ServicingCapacityRules.ActionState("queried", "assign"));
        Assert.Equal("approved", ServicingCapacityRules.ActionState("approved", "assign"));
        Assert.Throws<ArgumentException>(() => ServicingCapacityRules.ActionState("approved", "withdraw"));
        Assert.Throws<ArgumentException>(() => ServicingCapacityRules.ActionState("queued", "reopen"));
        Assert.Throws<ArgumentException>(() => ServicingCapacityRules.ActionState("superseded", "assign"));
        Assert.Throws<ArgumentException>(() => ServicingCapacityRules.ActionState("unknown", "assign"));
    }
}
