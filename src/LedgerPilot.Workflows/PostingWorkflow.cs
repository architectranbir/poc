using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace LedgerPilot.Workflows;

public sealed record PostingOutcome(string CommandKey, string Status, string? PostingReference = null);
public static class PostingWorkflow
{
    [Function(nameof(TrackPosting))]
    public static async Task<PostingOutcome> TrackPosting([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var key = context.GetInput<string>();
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Command key required.");
        var deadline = context.CurrentUtcDateTime.AddMinutes(30);
        var retry = TaskOptions.FromRetryPolicy(new RetryPolicy(3, TimeSpan.FromSeconds(5)));
        while (true) {
            PostingOutcome? receipt = null;
            try {
                receipt = await context.CallActivityAsync<PostingOutcome?>(nameof(PostingActivities.ReadPostingReceipt), key, retry);
            }
            catch (TaskFailedException) {
                // Read failures cannot authorize or cancel financial work. Resume on a durable timer.
                context.SetCustomStatus(new { Status = "ReceiptReadUnavailable" });
            }
            if (receipt is not null) {
                context.SetCustomStatus(new { Status = "Posted" });
                return receipt;
            }
            if (context.CurrentUtcDateTime >= deadline) {
                context.SetCustomStatus(new { Status = "ReconciliationRequired" });
                return new(key, "ReconciliationRequired");
            }
            var next = context.CurrentUtcDateTime.AddSeconds(30);
            await context.CreateTimer(next > deadline ? deadline : next, CancellationToken.None);
        }
    }
}
