using LedgerPilot.Persistence;
using Npgsql;

namespace LedgerPilot.Persistence.Tests;

[Collection("Database")]
public sealed class CommandStoreTests
{
    // Fail rather than silently skip when the integration database is unavailable.
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create(
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION")
        ?? throw new InvalidOperationException("POSTGRES_TEST_CONNECTION is required."));

    [Fact]
    public async Task ConcurrentAcceptanceAndConflictingReuse()
    {
        await using var source = Source(); var store = new CommandStore(source); await store.Initialize();
        var key = Guid.NewGuid().ToString();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.Accept(key, "payload")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Accept(key, "changed"));
        await using var count = source.CreateCommand("SELECT count(*) FROM lp_outbox WHERE command_key=$1");
        count.Parameters.AddWithValue(key);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task BrokerFailureRollsBackAndConcurrentPublishersDoNotShareClaim()
    {
        await using var source = Source(); var store = new CommandStore(source); await store.Initialize();
        // Drain other tests' pending commands before inspecting this unique command.
        while (await store.PublishNext((_, _, _) => Task.CompletedTask)) { }
        var key = Guid.NewGuid().ToString(); await store.Accept(key, "payload");
        await Assert.ThrowsAsync<IOException>(() => store.PublishNext((_, _, _) => throw new IOException("broker unavailable")));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = store.PublishNext(async (k, p, _) => { Assert.Equal(key, k); Assert.Equal("payload", p); entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False(await store.PublishNext((_, _, _) => throw new Exception("claimed twice"))); }
        finally { release.SetResult(); }
        Assert.True(await first);
        Assert.False(await store.PublishNext((_, _, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task DurableReceiptIsIdempotentAndRejectsChangedResultOrUnknownCommand()
    {
        await using var source = Source(); var store = new CommandStore(source); await store.Initialize();
        var key = Guid.NewGuid().ToString(); await store.Accept(key, "payload");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.Complete(key, "SAP-123")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Complete(key, "SAP-456"));
        await Assert.ThrowsAsync<PostgresException>(() => store.Complete(Guid.NewGuid().ToString(), "unknown"));
        await using var restarted = Source();
        Assert.Equal("SAP-123", await new CommandStore(restarted).Receipt(key));
    }
}
