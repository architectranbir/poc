using LedgerPilot.Domain;
namespace LedgerPilot.Domain.Tests;
public class FixedPriceCalculationsTests
{
 static DateOnly Cutoff=>new(2026,10,10);
 static BillingMilestone M(string id="M1",decimal amount=100m,decimal billed=0m,int day=10,bool cancelled=false)=>new(id,new DateOnly(2026,10,day),amount,billed,cancelled);
 [Fact] public void DueDateBoundaryIncluded(){var p=FixedPriceCalculations.Calculate(100,0,Cutoff,[M()],["M1"]);Assert.Equal(100,p.ProposedBill);}
 [Fact] public void PartialMilestoneBillsOnlyRemainder(){var p=FixedPriceCalculations.Calculate(100,40,Cutoff,[M(billed:40)],["M1"]);Assert.Equal(60,p.ProposedBill);Assert.Equal(60,p.RemainingContract);}
 [Fact] public void SelectionDeterminesBill(){var p=FixedPriceCalculations.Calculate(200,0,Cutoff,[M(),M("M2")],["M2"]);Assert.Equal(100,p.ProposedBill);Assert.Equal(["M2"],p.IncludedMilestones);}
 [Fact] public void FutureMilestoneRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M(day:11)],["M1"]));}
 [Fact] public void FullyBilledMilestoneRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,100,Cutoff,[M(billed:100)],["M1"]));}
 [Fact] public void CancelledMilestoneRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M(cancelled:true)],["M1"]));}
 [Fact] public void ContractOverflowRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,50,Cutoff,[M()],["M1"]));}
 [Fact] public void DuplicateSelectionRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M()],["M1","M1"]));}
 [Fact] public void UnknownSelectionRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M()],["M2"]));}
 [Fact] public void InconsistentHistoryRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M(billed:40)],["M1"]));}
 [Fact] public void ScheduleOverflowRejected(){Assert.Throws<ArgumentException>(()=>FixedPriceCalculations.Calculate(100,0,Cutoff,[M(),M("M2")],["M1"]));}
}
