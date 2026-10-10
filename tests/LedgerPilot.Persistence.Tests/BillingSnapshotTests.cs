using LedgerPilot.Persistence;
using Npgsql;

namespace LedgerPilot.Persistence.Tests;
[Collection("Database")]
public sealed class BillingSnapshotTests
{
    [Fact]
    public async Task SnapshotCommandAndOutboxCommitTogetherAndConflictRollsBack()
    {
        var connection = Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION")!;
        await using var source = NpgsqlDataSource.Create(connection);
        var commands = new CommandStore(source); await commands.Initialize();
        var key = Guid.NewGuid().ToString();
        var command = new SapCommand("CreateAndPost", key, "TEST", 10, "GBP", "state");
        using (var billing = new BillingSnapshotStore(connection)) {
            var old = billing.Load();
            billing.Save("accepted", command);
            await commands.VerifyAccepted(key, command.CanonicalPayload());
            Assert.Throws<InvalidOperationException>(() => billing.Save("must-rollback", command with { Net = 11 }));
            Assert.Equal("accepted", billing.Load());
            Assert.Throws<InvalidOperationException>(() => new BillingSnapshotStore(connection));
            await using var count = source.CreateCommand("SELECT count(*) FROM lp_outbox WHERE command_key=$1");
            count.Parameters.AddWithValue(key); Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        using (var restarted = new BillingSnapshotStore(connection)) {
            Assert.Equal("accepted", restarted.Load());
            restarted.Save("restart-success");
        }
        await using var cleanup = source.CreateCommand("DELETE FROM lp_billing_snapshot WHERE id=1");
        await cleanup.ExecuteNonQueryAsync();
    }
}
