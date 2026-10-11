using LedgerPilot.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LedgerPilot.Workflows;
public sealed class PostingActivities(CommandStore store, PostingReceiptReader reader, ILogger<PostingActivities> logger)
{
    [Function(nameof(ReadPostingReceipt))]
    public async Task<PostingOutcome?> ReadPostingReceipt([ActivityTrigger] string commandKey, CancellationToken ct)
    {
        var receipt = await reader.Read(commandKey, ct);
        logger.LogInformation("PostingReceiptObserved InstanceId={InstanceId} Status={Status}",
            InstanceId(commandKey), receipt?.Status ?? "Pending");
        return receipt;
    }

    // Database acceptance is the authority. This timer never accepts commands or approves bills.
    [Function(nameof(StartAcceptedPostings))]
    public async Task StartAcceptedPostings([TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client, CancellationToken ct)
    {
        foreach (var key in await store.UntrackedPostings(ct)) {
            var id = InstanceId(key);
            if (await client.GetInstanceAsync(id, cancellation: ct) is null)
                await client.ScheduleNewOrchestrationInstanceAsync(nameof(PostingWorkflow.TrackPosting), key,
                    new StartOrchestrationOptions { InstanceId = id }, ct);
            await store.MarkPostingTracked(key, ct);
            logger.LogInformation("PostingWorkflowTracked InstanceId={InstanceId}", id);
        }
    }
    public static string InstanceId(string key) => "posting-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
