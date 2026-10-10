using LedgerPilot.Domain;
namespace LedgerPilot.Domain.Tests;
public class SubmissionGateTests
{
    static StateApproval Intent(string hash = "current") => new(hash, "requestor", "Requestor");
    static ValidationFinding Finding(FindingSeverity severity, string hash = "dep1") => new("line-1", "W-03", severity, hash);
    [Fact] public void BlockCannotBeAcknowledgedAway() {
        var result = SubmissionGate.Evaluate("current", [Finding(FindingSeverity.Block)], [new("line-1", "dep1", "user")], false, null, Intent());
        Assert.False(result.Allowed); Assert.Contains("W-03:line-1:blocked", result.Reasons);
    }
    [Fact] public void ConfirmationRequiresCurrentDependency() {
        Assert.False(SubmissionGate.Evaluate("current", [Finding(FindingSeverity.Confirm, "dep2")], [new("line-1", "dep1", "user")], false, null, Intent()).Allowed);
        Assert.True(SubmissionGate.Evaluate("current", [Finding(FindingSeverity.Confirm)], [new("line-1", "dep1", "user")], false, null, Intent()).Allowed);
    }
    [Fact] public void UnrelatedStateEditPreservesFindingAcknowledgementButRequiresNewIntent() {
        Assert.True(SubmissionGate.Evaluate("new-state", [Finding(FindingSeverity.Confirm)], [new("line-1", "dep1", "user")], false, null, Intent("new-state")).Allowed);
    }
    [Theory] [InlineData(null, "Finance")] [InlineData("old", "Finance")] [InlineData("current", "Requestor")]
    public void FinanceGateRejectsMissingStaleOrWrongRole(string? hash, string role) {
        Assert.False(SubmissionGate.Evaluate("current", [], [], true, hash is null ? null : new(hash, "user", role), Intent()).Allowed);
    }
    [Fact] public void CurrentFinanceAndHumanIntentPass() {
        Assert.True(SubmissionGate.Evaluate("current", [Finding(FindingSeverity.Advisory)], [], true, new("current", "finance", "Finance"), Intent()).Allowed);
    }
    [Theory] [InlineData(null)] [InlineData("old")]
    public void MissingOrStaleHumanIntentFails(string? hash) {
        Assert.False(SubmissionGate.Evaluate("current", [], [], false, null, hash is null ? null : Intent(hash)).Allowed);
    }
    [Fact] public void BlankActorCannotConfirm() {
        Assert.False(SubmissionGate.Evaluate("current", [Finding(FindingSeverity.Confirm)], [new("line-1", "dep1", "")], false, null, Intent()).Allowed);
    }
}
