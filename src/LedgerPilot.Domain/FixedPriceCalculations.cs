namespace LedgerPilot.Domain;

public sealed record BillingMilestone(string Id,DateOnly DueDate,decimal Amount,decimal AlreadyBilled,bool Cancelled=false);
public sealed record FixedPricePosition(decimal ContractValue,decimal PreviouslyBilled,decimal RemainingContract,
    decimal ProposedBill,IReadOnlyList<string> IncludedMilestones);

public static class FixedPriceCalculations
{
    public static FixedPricePosition Calculate(decimal contractValue,decimal previouslyBilled,DateOnly cutoff,
        IEnumerable<BillingMilestone> schedule,IEnumerable<string> selected)
    {
        if(contractValue<0||previouslyBilled<0||previouslyBilled>contractValue)
            throw new ArgumentException("Invalid contract value or previous billing.");
        var milestones=schedule.ToArray();
        if(milestones.Any(m=>string.IsNullOrWhiteSpace(m.Id)||m.Amount<0||m.AlreadyBilled<0||m.AlreadyBilled>m.Amount))
            throw new ArgumentException("Invalid milestone amounts or identity.");
        if(milestones.Select(m=>m.Id).Distinct(StringComparer.Ordinal).Count()!=milestones.Length)
            throw new ArgumentException("Duplicate milestone identity.");
        if(milestones.Sum(m=>m.Amount)>contractValue)
            throw new ArgumentException("Milestone schedule exceeds contract value.");
        if(milestones.Sum(m=>m.AlreadyBilled)>previouslyBilled)
            throw new ArgumentException("Milestone history exceeds previous contract billing.");
        var ids=selected.ToArray();
        if(ids.Length==0||ids.Any(string.IsNullOrWhiteSpace)||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)
            throw new ArgumentException("Select unique milestone identities.");
        var byId=milestones.ToDictionary(m=>m.Id,StringComparer.Ordinal);
        decimal proposed=0;
        foreach(var id in ids){
            if(!byId.TryGetValue(id,out var m))throw new ArgumentException("Unknown milestone.");
            if(m.Cancelled||m.DueDate>cutoff||m.AlreadyBilled==m.Amount)
                throw new ArgumentException("Selected milestone is cancelled, not due or fully billed.");
            proposed+=m.Amount-m.AlreadyBilled;
        }
        var remaining=contractValue-previouslyBilled;
        if(proposed>remaining)throw new ArgumentException("Proposed bill exceeds remaining contract value.");
        return new(contractValue,previouslyBilled,remaining,proposed,ids.OrderBy(x=>x,StringComparer.Ordinal).ToArray());
    }
}
