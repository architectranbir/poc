using LedgerPilot.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using System.Security.Cryptography;
using System.Text;

namespace LedgerPilot.Workflows;
public sealed class PostingActivities(CommandStore store, PostingReceiptReader reader)
{
    [Function(nameof(ReadPostingReceipt))]
    public Task<PostingOutcome?> ReadPostingReceipt([ActivityTrigger] string commandKey, CancellationToken ct) => reader.Read(commandKey, ct);

    // Database acceptance is the authority. This timer never accepts commands or approves bills.
    [Function(nameof(StartAcceptedPostings))]
    public async Task StartAcceptedPostings([TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client, CancellationToken ct)
    {
        foreach (var key in await store.PendingPostings(ct)) {
            var id = InstanceId(key);
            if (await client.GetInstanceAsync(id, cancellation: ct) is null)
                await client.ScheduleNewOrchestrationInstanceAsync(nameof(PostingWorkflow.TrackPosting), key,
                    new StartOrchestrationOptions { InstanceId = id }, ct);
        }
    }
    public static string InstanceId(string key) => "posting-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
