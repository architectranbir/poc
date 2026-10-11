using LedgerPilot.Persistence;
using Npgsql;

namespace LedgerPilot.Persistence.Tests;

[Collection("Database")]
public sealed class WorkflowDiscoveryTests
{
    static NpgsqlDataSource Source() => NpgsqlDataSource.Create(
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION")
        ?? throw new InvalidOperationException("POSTGRES_TEST_CONNECTION is required."));

    [Fact]
    public async Task ReceiptBeforeTimerRemainsDiscoverableUntilTrackingIsDurable()
    {
        await using var source = Source(); var store = new CommandStore(source); await store.Initialize();
        var key = "post:" + Guid.NewGuid();
        await store.Accept(key, new SapCommand("CreateAndPost", key, "fixture", 10m, "GBP", "state").CanonicalPayload());
        // The SAP worker has finished before the one-minute timer first discovers this command.
        await store.Complete(key, "receipt-already-committed");
        Assert.Contains(key, await store.UntrackedPostings());

        // Scheduling succeeded, but a process crash occurred before the tracking insert.
        // A fresh timer must rediscover the key and inspect the same Durable instance ID.
        await using var restarted = Source(); var recovered = new CommandStore(restarted);
        Assert.Contains(key, await recovered.UntrackedPostings());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => recovered.MarkPostingTracked(key)));
        Assert.DoesNotContain(key, await store.UntrackedPostings());
        await using var count = source.CreateCommand("SELECT count(*) FROM lp_workflow_tracking WHERE command_key=$1");
        count.Parameters.AddWithValue(key);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
        Assert.Equal("receipt-already-committed", await recovered.Receipt(key));
    }

    [Fact]
    public async Task NonPostingFixturesCannotConsumeTheDiscoveryBatch()
    {
        await using var source = Source(); var store = new CommandStore(source); await store.Initialize();
        var prefix = Guid.NewGuid().ToString();
        await using var fixtures = source.CreateCommand("""
            INSERT INTO lp_command(command_key,payload,accepted_at)
            SELECT $1 || '-' || n, 'not-json', now()-interval '1 day'
            FROM generate_series(1,1001) n
            """);
        fixtures.Parameters.AddWithValue(prefix); await fixtures.ExecuteNonQueryAsync();
        var key = "post:" + Guid.NewGuid();
        var createKey = "create:" + Guid.NewGuid();
        await store.Accept(createKey, new SapCommand("Create", createKey, "fixture", 10m, "GBP", "state").CanonicalPayload());
        await store.Accept(key, new SapCommand("CreateAndPost", key, "fixture", 10m, "GBP", "state").CanonicalPayload());
        var candidates = await store.UntrackedPostings();
        Assert.Contains(key, candidates);
        Assert.DoesNotContain(createKey, candidates);
        Assert.DoesNotContain(candidates, value => value.StartsWith(prefix));
    }
}
