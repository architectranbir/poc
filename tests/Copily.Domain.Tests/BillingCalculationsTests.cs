using System.Globalization;
using Copily.Domain;

namespace Copily.Domain.Tests;

public sealed class BillingCalculationsTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    private static AdjustmentEvent Event(string id, string line, AdjustmentKind kind, decimal amount) =>
        new(id, line, kind, amount, "synthetic-requestor", "Reviewed allocation", At);

    [Fact]
    public void W01_TimeMultipliesQuantityWhileExpenseUsesAmountOnly()
    {
        var p = BillingCalculations.Replay([
            new("T", WipKind.Time, 2.5m, 120m, 0m),
            new("E", WipKind.Expense, 99m, 999m, 45m)], []);
        Assert.Equal(345m, p.Bill);
    }

    [Fact]
    public void W08_P01_P07_V12_ReplayReconcilesEveryLineAndRevenueImpact()
    {
        SourceLine[] source = [new("T", WipKind.Time, 10m, 100m, 0m), new("E", WipKind.Expense, 1m, 0m, 200m)];
        AdjustmentEvent[] events = [Event("1", "T", AdjustmentKind.Postpone, 150m),
            Event("2", "T", AdjustmentKind.WriteOff, 250m), Event("3", "E", AdjustmentKind.WriteOff, 40m)];
        var first = BillingCalculations.Replay(source, events, 50m);
        var restart = BillingCalculations.Replay(source, events, 50m);
        Assert.Equal(810m, first.Bill);
        Assert.Equal(290m, first.WriteOff);
        Assert.Equal(-390m, first.RevenueImpact);
        Assert.All(first.Lines, l => Assert.Equal(l.Source, l.Bill + l.Postpone + l.WriteOff));
        Assert.Equal(first.Basis, first.Bill - first.WriteUp + first.Postpone + first.WriteOff);
        Assert.Equal(BillingCalculations.FinancialHash("P", first), BillingCalculations.FinancialHash("P", restart));
    }

    [Fact]
    public void W08_DuplicateEventDeliveryDoesNotApplyTwice()
    {
        var e = Event("same", "T", AdjustmentKind.WriteOff, 10m);
        var p = BillingCalculations.Replay([new("T", WipKind.Time, 1m, 100m, 0m)], [e, e]);
        Assert.Equal(90m, p.Bill);
    }

    [Fact]
    public void W08_ReusedEventIdentityWithDifferentContentIsRejected()
    {
        var e = Event("same", "T", AdjustmentKind.WriteOff, 10m);
        Assert.Throws<ArgumentException>(() => BillingCalculations.Replay(
            [new("T", WipKind.Time, 1m, 100m, 0m)], [e, e with { Amount = 20m }]));
    }

    [Fact]
    public void W06_OverAllocationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => BillingCalculations.Replay(
            [new("T", WipKind.Time, 1m, 100m, 0m)],
            [Event("1", "T", AdjustmentKind.WriteOff, 101m)]));
    }

    [Theory]
    [InlineData("Tax & Legal", 10000, false)]
    [InlineData("Tax & Legal", 10000.01, true)]
    [InlineData("Audit", 9999.99, false)]
    [InlineData("Audit", 10000, false)]
    [InlineData("Audit", 10000.01, true)]
    [InlineData("Advisory", 20000, false)]
    [InlineData("Advisory", 20000.01, true)]
    public void P03_P04_P05_P06_StrictThresholdBoundary(string unit, double amount, bool expected)
    {
        var value = (decimal)amount;
        Assert.Equal(expected, BillingCalculations.NeedsFinanceApproval(unit, value, 0m));
        Assert.Equal(expected, BillingCalculations.NeedsFinanceApproval(unit, 0m, value));
    }

    [Fact]
    public void V12_DeclaredRoundingIsApplied()
    {
        Assert.Equal(1.01m, BillingCalculations.RoundMoney(1.005m));
        Assert.Equal(-1.01m, BillingCalculations.RoundMoney(-1.005m));
    }

    [Fact]
    public void HashIsIndependentOfCultureLineOrderAndDecimalScale()
    {
        var previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            var first = BillingCalculations.Replay([new("A", WipKind.Expense, 1m, 0m, 10.00m),
                new("B", WipKind.Expense, 1m, 0m, 20m)], []);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = BillingCalculations.Replay([new("B", WipKind.Expense, 1m, 0m, 20.00m),
                new("A", WipKind.Expense, 1m, 0m, 10m)], []);
            Assert.Equal(BillingCalculations.FinancialHash("P", first), BillingCalculations.FinancialHash("P", second));
        } finally { CultureInfo.CurrentCulture = previous; }
    }
}

