using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalTaskTests
{
    [Fact]
    public void TaskCapabilitiesDoNotGrantExternalIdentityOrParentReadAuthority()
    {
        var roles = new HashSet<string> { "servicing" };
        var actor = new BackOffice.Application.ActorContext(Guid.NewGuid(), null, null, roles);
        Assert.True(actor.HasCapability("task-write"));
        Assert.False(actor.HasCapability("agency-read"));
        Assert.False((actor with { AgencyId = Guid.NewGuid() }).HasCapability("task-write"));
        Assert.False((actor with { Roles = new HashSet<string> { "finance" } }).HasCapability("task-read"));
    }
    [Theory]
    [InlineData("complaint")]
    [InlineData("agency-onboarding")]
    [InlineData("underwriting-referral")]
    public void SourceTypesAreAcceptedWithoutChangingBusinessMeaning(string type)
        => TaskRules.ValidateWrite(new(type, "Review evidence", "normal", new("unassigned"), null));

    [Fact]
    public void AssignmentHasExactlyOneTypedTarget()
    {
        TaskRules.ValidateAssignment(new("user", Guid.NewGuid()));
        TaskRules.ValidateAssignment(new("team", TeamId: Guid.NewGuid()));
        foreach (var bad in new TaskAssignment[] { new("user"), new("team", Guid.NewGuid()), new("unassigned", Guid.NewGuid()), new("team", TeamId: Guid.Empty), new("user", Guid.NewGuid(), Guid.NewGuid()), new("everyone") })
            Assert.Throws<TaskRuleException>(() => TaskRules.ValidateAssignment(bad));
    }

    [Theory]
    [InlineData("", "Review", "normal")]
    [InlineData("complaint", "   ", "normal")]
    [InlineData("complaint", "Review", "medium")]
    [InlineData("made-up", "Review", "urgent")]
    public void InvalidTaskFieldsFail(string type, string title, string priority)
        => Assert.Throws<TaskRuleException>(() => TaskRules.ValidateWrite(new(type, title, priority, new("unassigned"), null)));

    [Fact]
    public void CompletedChecklistRequiredBeforeCompletionAndExplicitReopenKeepsTerminalHistorySeparate()
    {
        Assert.Throws<TaskRuleException>(() => TaskRules.ValidateTransition("open", "completed", "Reviewed", [new(Guid.NewGuid(), true, false)]));
        TaskRules.ValidateTransition("open", "completed", "Reviewed", [new(Guid.NewGuid(), true, true), new(Guid.NewGuid(), false, false)]);
        TaskRules.ValidateTransition("completed", "open", "New evidence received", []);
        Assert.Throws<TaskRuleException>(() => TaskRules.ValidateTransition("completed", "in-progress", "Continue", []));
        Assert.Throws<TaskRuleException>(() => TaskRules.ValidateTransition("cancelled", "completed", "Override", []));
    }

    [Theory]
    [InlineData("open", "completed", "")]
    [InlineData("open", "open", "Duplicate")]
    [InlineData("open", "overdue", "Late")]
    [InlineData("completed", "open", "  ")]
    public void TransitionRejectsBlankReasonDerivedOrUnchangedState(string before, string after, string reason)
        => Assert.Throws<TaskRuleException>(() => TaskRules.ValidateTransition(before, after, reason, []));

    [Fact]
    public void BulkRejectsDuplicateIdEvenWhenVersionsDifferAndRequiresBoundedStrongVersions()
    {
        var id = Guid.NewGuid();
        TaskRules.ValidateSelection([new(id, "\"v1\"")]);
        foreach (var bad in new TaskSelection[][] { [], [new(id, "\"v1\""), new(id, "\"v2\"")], [new(id, "*")], [new(id, "W/\"weak\"")], [new(Guid.Empty, "\"v1\"")], Enumerable.Range(0, 101).Select(_ => new TaskSelection(Guid.NewGuid(), "\"v1\"")).ToArray() })
            Assert.Throws<TaskRuleException>(() => TaskRules.ValidateSelection(bad));
    }

    [Fact]
    public void ChecklistUpdatesCannotInventItemsOrRepeatAnItem()
    {
        var id = Guid.NewGuid();
        TaskRules.ValidateChecklist([new(id, true, false)], [new(id, true)]);
        Assert.Throws<TaskRuleException>(() => TaskRules.ValidateChecklist([new(id, true, false)], [new(Guid.NewGuid(), true)]));
        Assert.Throws<TaskRuleException>(() => TaskRules.ValidateChecklist([new(id, true, false)], [new(id, true), new(id, false)]));
    }

    [Fact]
    public void OverdueUsesLocalDayAndExcludesTerminalStates()
    {
        var today = new DateOnly(2026, 9, 21);
        Assert.True(TaskRules.IsOverdue("awaiting-information", today.AddDays(-1), today));
        Assert.False(TaskRules.IsOverdue("open", today, today));
        Assert.False(TaskRules.IsOverdue("open", null, today));
        Assert.False(TaskRules.IsOverdue("completed", today.AddDays(-1), today));
        Assert.False(TaskRules.IsOverdue("cancelled", today.AddDays(-1), today));
    }
}
