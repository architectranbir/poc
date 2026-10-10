using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace LedgerPilot.Domain;

public enum WipKind { Time, Expense }
public enum AdjustmentKind { Postpone, WriteOff }
public sealed record SourceLine(string Id, WipKind Kind, decimal Quantity, decimal Rate, decimal ExpenseAmount)
{
    // Expenses are amount-only: their quantity is never a multiplier.
    public decimal Value => Kind == WipKind.Time ? Quantity * Rate : ExpenseAmount;
}
public sealed record AdjustmentEvent(string EventId, string LineId, AdjustmentKind Kind, decimal Amount,
    string Actor, string Rationale, DateTimeOffset At);
public sealed record LineAllocation(string LineId, decimal Source, decimal Bill, decimal Postpone, decimal WriteOff);
public sealed record FinancialPosition(decimal Basis, decimal Bill, decimal Postpone, decimal WriteOff,
    decimal WriteUp, decimal RevenueImpact, IReadOnlyList<LineAllocation> Lines);

public static class BillingCalculations
{
    public static FinancialPosition Replay(IEnumerable<SourceLine> source, IEnumerable<AdjustmentEvent> events,
        decimal writeUp = 0m)
    {
        if (writeUp < 0m) throw new ArgumentOutOfRangeException(nameof(writeUp));
        var lines = source.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (lines.Values.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Quantity < 0m ||
            x.Rate < 0m || x.ExpenseAmount < 0m)) throw new ArgumentException("Invalid source line.");
        var allocations = lines.Values.ToDictionary(x => x.Id,
            x => new LineAllocation(x.Id, x.Value, x.Value, 0m, 0m), StringComparer.Ordinal);
        var seen = new Dictionary<string, AdjustmentEvent>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (string.IsNullOrWhiteSpace(e.EventId) || string.IsNullOrWhiteSpace(e.Actor) ||
                string.IsNullOrWhiteSpace(e.Rationale) || e.Amount <= 0m)
                throw new ArgumentException("An adjustment requires a positive amount, identity and rationale.");
            if (seen.TryGetValue(e.EventId, out var original))
            {
                if (original != e) throw new ArgumentException("Event ID reused with different content.");
                continue; // replayed delivery of the same immutable event
            }
            seen.Add(e.EventId, e);
            if (!allocations.TryGetValue(e.LineId, out var a)) throw new ArgumentException("Unknown source line.");
            if (e.Amount > a.Bill) throw new ArgumentException("Adjustment exceeds remaining source value.");
            allocations[e.LineId] = e.Kind switch
            {
                AdjustmentKind.Postpone => a with { Bill = a.Bill - e.Amount, Postpone = a.Postpone + e.Amount },
                AdjustmentKind.WriteOff => a with { Bill = a.Bill - e.Amount, WriteOff = a.WriteOff + e.Amount },
                _ => throw new ArgumentException("Unknown adjustment kind.")
            };
        }
        var values = allocations.Values.OrderBy(x => x.LineId, StringComparer.Ordinal).ToArray();
        var basis = values.Sum(x => x.Source);
        var remaining = values.Sum(x => x.Bill);
        return new(basis, remaining + writeUp, values.Sum(x => x.Postpone), values.Sum(x => x.WriteOff),
            writeUp, remaining + writeUp - basis, values);
    }

    public static decimal Threshold(string businessUnit) => businessUnit switch
    {
        "Tax & Legal" or "Audit" => 10000m,
        "Advisory" => 20000m,
        _ => throw new ArgumentException("Unknown business unit.")
    };
    public static bool NeedsFinanceApproval(string unit, decimal writeOff, decimal writeUp)
    {
        if (writeOff < 0m || writeUp < 0m) throw new ArgumentOutOfRangeException(nameof(writeOff));
        var threshold = Threshold(unit);
        return writeOff > threshold || writeUp > threshold;
    }
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    // Deterministic, culture-independent snapshot for the initial source/adjustment slice.
    // Full commercial/approval state hashing is a later milestone.
    public static string FinancialHash(string projectId, FinancialPosition p)
    {
        static string D(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new {
            schema = 1, projectId, basis = D(p.Basis), bill = D(p.Bill),
            postpone = D(p.Postpone), writeOff = D(p.WriteOff), writeUp = D(p.WriteUp),
            lines = p.Lines.OrderBy(x => x.LineId, StringComparer.Ordinal).Select(x => new {
                x.LineId, source = D(x.Source), bill = D(x.Bill),
                postpone = D(x.Postpone), writeOff = D(x.WriteOff)
            })
        });
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }
}

