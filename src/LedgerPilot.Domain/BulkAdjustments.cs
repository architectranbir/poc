namespace LedgerPilot.Domain;

public sealed record AdjustmentSelection(string LineId, decimal Amount);
public sealed record AdjustmentBatch(string BatchId, AdjustmentKind Kind, string Actor, string Rationale,
    string StateHash, IReadOnlyList<AdjustmentSelection> Items);
public sealed class StaleAdjustmentStateException() : ArgumentException("WIP changed. Review the current values before confirming the batch.");

public static class BulkAdjustments
{
    public static AdjustmentEvent[] Prepare(AdjustmentBatch batch, IEnumerable<SourceLine> source,
        IReadOnlyList<AdjustmentEvent> existing, string currentHash, DateTimeOffset at, decimal writeUp = 0m)
    {
        if (!Guid.TryParseExact(batch.BatchId, "D", out _) || batch.Items is null || batch.Items.Count is < 1 or > 500 ||
            string.IsNullOrWhiteSpace(batch.Actor) || string.IsNullOrWhiteSpace(batch.Rationale) ||
            string.IsNullOrWhiteSpace(batch.StateHash) || !Enum.IsDefined(batch.Kind))
            throw new ArgumentException("A batch requires an identity, reason, state hash and 1–500 selected lines.");
        if (batch.Items.Any(x => x is null || string.IsNullOrWhiteSpace(x.LineId) || x.Amount <= 0m) ||
            batch.Items.Select(x => x.LineId).Distinct(StringComparer.Ordinal).Count() != batch.Items.Count)
            throw new ArgumentException("Select each line once with a positive adjustment amount.");
        var prefix = "bulk:" + batch.BatchId + ":";
        var committed = existing.Where(x => x.EventId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        var proposed = batch.Items.OrderBy(x => x.LineId, StringComparer.Ordinal).Select(x => {
            var original = committed.FirstOrDefault(e => e.LineId == x.LineId);
            return new AdjustmentEvent(prefix + x.LineId, x.LineId, batch.Kind, x.Amount,
                batch.Actor, batch.Rationale, original?.At ?? at);
        }).ToArray();
        if (committed.Length > 0)
        {
            if (committed.Length != proposed.Length || proposed.Any(x => !committed.Contains(x)))
                throw new ArgumentException("Batch identity reused with different content.");
            return []; // Previously committed, even when the current state has subsequently changed.
        }
        if (batch.StateHash != currentHash) throw new StaleAdjustmentStateException();
        BillingCalculations.Replay(source, existing.Concat(proposed), writeUp); // Validate every line before committing any.
        return proposed;
    }
}
