namespace LedgerPilot.Domain;

public enum FindingSeverity { Advisory, Confirm, Block }
public sealed record ValidationFinding(string Id, string RuleId, FindingSeverity Severity, string DependencyHash);
public sealed record FindingAcknowledgement(string FindingId, string DependencyHash, string Actor);
public sealed record StateApproval(string StateHash, string Actor, string Role);
public sealed record SubmissionDecision(bool Allowed, IReadOnlyList<string> Reasons);

// These decisions are pure. Callers must authenticate identities and obtain state
// hashes from authoritative persisted inputs before invoking this gate.
public static class SubmissionGate
{
    public static SubmissionDecision Evaluate(string stateHash,
        IEnumerable<ValidationFinding> findings, IEnumerable<FindingAcknowledgement> acknowledgements,
        bool financeRequired, StateApproval? financeApproval, StateApproval? humanIntent)
    {
        if (string.IsNullOrWhiteSpace(stateHash)) throw new ArgumentException("State hash required.");
        var accepted = acknowledgements.ToArray();
        var reasons = new List<string>();
        foreach (var finding in findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Id) || string.IsNullOrWhiteSpace(finding.DependencyHash))
                throw new ArgumentException("Finding identity and dependency hash required.");
            switch (finding.Severity)
            {
                case FindingSeverity.Block:
                    reasons.Add($"{finding.RuleId}:{finding.Id}:blocked"); break;
                case FindingSeverity.Confirm:
                    if (!accepted.Any(a => a.FindingId == finding.Id && a.DependencyHash == finding.DependencyHash &&
                        !string.IsNullOrWhiteSpace(a.Actor)))
                        reasons.Add($"{finding.RuleId}:{finding.Id}:confirmation-required");
                    break;
                case FindingSeverity.Advisory: break;
                default: throw new ArgumentException("Unknown finding severity.");
            }
        }
        if (financeRequired && !Matches(financeApproval, stateHash, "Finance")) reasons.Add("finance-approval-required");
        if (!Matches(humanIntent, stateHash, "Requestor")) reasons.Add("exact-state-human-intent-required");
        return new(reasons.Count == 0, reasons);
    }

    public static bool Matches(StateApproval? approval, string stateHash, string role) =>
        approval is not null && !string.IsNullOrWhiteSpace(approval.Actor) &&
        approval.StateHash == stateHash && approval.Role == role;
}
