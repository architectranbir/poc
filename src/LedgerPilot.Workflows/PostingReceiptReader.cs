using LedgerPilot.Persistence;
using System.Text.Json;

namespace LedgerPilot.Workflows;
public sealed class PostingReceiptReader(CommandStore store)
{
    public async Task<PostingOutcome?> Read(string key, CancellationToken ct = default)
    {
        var payload = await store.AcceptedPayload(key, ct) ?? throw new InvalidDataException("Unknown accepted command.");
        var command = JsonSerializer.Deserialize<SapCommand>(payload) ?? throw new InvalidDataException("Invalid accepted command.");
        command.Validate();
        if (command.Operation != "CreateAndPost") throw new InvalidDataException("Not a confirmed posting command.");
        var receipt = await store.Receipt(key, ct);
        if (receipt is null) return null;
        using var json = JsonDocument.Parse(receipt);
        var doc = json.RootElement; var request = doc.GetProperty("request");
        var reference = doc.GetProperty("postingReference").GetString();
        if (doc.GetProperty("status").GetString() != "POSTED" || doc.GetProperty("postingCommandKey").GetString() != key ||
            request.GetProperty("commandKey").GetString() != key || request.GetProperty("projectId").GetString() != command.ProjectId ||
            request.GetProperty("net").GetDecimal() != command.Net || request.GetProperty("currency").GetString() != command.Currency ||
            request.GetProperty("stateHash").GetString() != command.StateHash || string.IsNullOrWhiteSpace(reference))
            throw new InvalidDataException("Posting receipt differs from accepted command.");
        return new(key, "Posted", reference);
    }
}
