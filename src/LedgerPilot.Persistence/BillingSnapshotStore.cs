using Npgsql;

namespace LedgerPilot.Persistence;

// Conservative single-writer PoC snapshot. This is not a horizontally scaled billing model.
public sealed class BillingSnapshotStore : IDisposable
{
    readonly NpgsqlConnection connection;
    long revision;
    public BillingSnapshotStore(string connectionString)
    {
        // Session advisory locks must not survive by returning a connection to the pool.
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        connection = new NpgsqlConnection(settings.ConnectionString);
        try {
            connection.Open();
            using var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock(741286019)", connection);
            if (!(bool)claim.ExecuteScalar()!) throw new InvalidOperationException("Billing database already has a writer.");
            using var schema = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS lp_billing_snapshot (id integer PRIMARY KEY CHECK(id=1), revision bigint NOT NULL, payload text NOT NULL)", connection);
            schema.ExecuteNonQuery();
        } catch { connection.Dispose(); throw; }
    }
    public string? Load()
    {
        using var cmd = new NpgsqlCommand("SELECT revision,payload FROM lp_billing_snapshot WHERE id=1", connection);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) { revision = 0; return null; }
        revision = reader.GetInt64(0); return reader.GetString(1);
    }
    public void Save(string snapshot, SapCommand? command = null)
    {
        using var tx = connection.BeginTransaction();
        using var update = new NpgsqlCommand("""
            INSERT INTO lp_billing_snapshot(id,revision,payload) VALUES (1,1,$1)
            ON CONFLICT(id) DO UPDATE SET revision=lp_billing_snapshot.revision+1,payload=EXCLUDED.payload
            WHERE lp_billing_snapshot.revision=$2 RETURNING revision
            """, connection, tx);
        update.Parameters.AddWithValue(snapshot); update.Parameters.AddWithValue(revision);
        var next = update.ExecuteScalar();
        if (next is null || (revision != 0 && (long)next != revision + 1))
            throw new InvalidOperationException("Billing snapshot revision changed.");
        if (command is not null) {
            command.Validate();
            var payload = command.CanonicalPayload();
            using var insert = new NpgsqlCommand("INSERT INTO lp_command(command_key,payload) VALUES ($1,$2) ON CONFLICT DO NOTHING", connection, tx);
            insert.Parameters.AddWithValue(command.CommandKey); insert.Parameters.AddWithValue(payload); insert.ExecuteNonQuery();
            using var check = new NpgsqlCommand("SELECT payload FROM lp_command WHERE command_key=$1 FOR UPDATE", connection, tx);
            check.Parameters.AddWithValue(command.CommandKey);
            if ((string?)check.ExecuteScalar() != payload) throw new InvalidOperationException("Command key content conflict.");
            using var outbox = new NpgsqlCommand("INSERT INTO lp_outbox(command_key) VALUES ($1) ON CONFLICT DO NOTHING", connection, tx);
            outbox.Parameters.AddWithValue(command.CommandKey); outbox.ExecuteNonQuery();
        }
        tx.Commit(); revision = (long)next;
    }
    public void Dispose() => connection.Dispose();
}
