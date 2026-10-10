using LedgerPilot.Domain;
namespace LedgerPilot.Domain.Tests;
public sealed class BulkAdjustmentTests
{
    static readonly SourceLine[] Source = [new("T",WipKind.Time,2m,100m,0m),new("E",WipKind.Expense,1m,0m,50m)];
    static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    static AdjustmentBatch Batch() => new("94876cd5-d377-4d43-a7b1-d0c73808172d",AdjustmentKind.WriteOff,"requestor","Reviewed expenses and time","current",[new("T",20m),new("E",10m)]);
    [Fact] public void W06_W08_W10_BatchReconcilesTimeAndAmountOnlyExpenses()
    {
        var events=BulkAdjustments.Prepare(Batch(),Source,[],"current",At);
        var p=BillingCalculations.Replay(Source,events);
        Assert.Equal(220m,p.Bill);Assert.Equal(30m,p.WriteOff);
        Assert.All(events,e=>{Assert.Equal("requestor",e.Actor);Assert.Equal(At,e.At);Assert.Equal(Batch().Rationale,e.Rationale);});
    }
    [Fact] public void W06_InvalidLastLineRejectsEntireBatch()
    {
        Assert.Throws<ArgumentException>(()=>BulkAdjustments.Prepare(Batch() with {Items=[new("T",20m),new("E",51m)]},Source,[],"current",At));
        Assert.Equal(250m,BillingCalculations.Replay(Source,[]).Bill);
    }
    [Fact] public void W06_StaleSelectionMustBeReviewedAgain()
    {Assert.Throws<StaleAdjustmentStateException>(()=>BulkAdjustments.Prepare(Batch(),Source,[],"changed",At));}
    [Fact] public void W06_P18_RetryAfterStateChangeDoesNotApplyTwice()
    {
        var events=BulkAdjustments.Prepare(Batch(),Source,[],"current",At);
        Assert.Empty(BulkAdjustments.Prepare(Batch(),Source,events,"changed",At.AddDays(1)));
    }
    [Fact] public void W06_P18_IdentityCannotBeReusedWithDifferentSubsetOrReason()
    {
        var events=BulkAdjustments.Prepare(Batch(),Source,[],"current",At);
        Assert.Throws<ArgumentException>(()=>BulkAdjustments.Prepare(Batch() with {Items=[new("T",20m)]},Source,events,"changed",At));
        Assert.Throws<ArgumentException>(()=>BulkAdjustments.Prepare(Batch() with {Rationale="Changed"},Source,events,"changed",At));
    }
    [Fact] public void W06_DuplicateSelectionAndUnknownSourceAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>BulkAdjustments.Prepare(Batch() with {Items=[new("T",20m),new("T",20m)]},Source,[],"current",At));
        Assert.Throws<ArgumentException>(()=>BulkAdjustments.Prepare(Batch() with {Items=[new("unknown",20m)]},Source,[],"current",At));
    }
}
