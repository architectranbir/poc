using Npgsql;
namespace LedgerPilot.Persistence;
public sealed class SimulatorSnapshotStore : IDisposable
{
    readonly NpgsqlConnection connection;
    long revision;
    public SimulatorSnapshotStore(string connectionString)
    {
        connection = new(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        try {
            connection.Open();
            using var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock(741286020)", connection);
            if (!(bool)claim.ExecuteScalar()!) throw new InvalidOperationException("Simulator database already has a writer.");
            using var schema = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS lp_simulator_snapshot (id integer PRIMARY KEY CHECK(id=1), revision bigint NOT NULL, payload text NOT NULL)", connection);
            schema.ExecuteNonQuery();
        } catch { connection.Dispose(); throw; }
    }
    public string? Load()
    {
        using var cmd = new NpgsqlCommand("SELECT revision,payload FROM lp_simulator_snapshot WHERE id=1", connection);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) { revision = 0; return null; }
        revision = reader.GetInt64(0); return reader.GetString(1);
    }
    public void Save(string snapshot)
    {
        using var cmd = new NpgsqlCommand("""
            INSERT INTO lp_simulator_snapshot(id,revision,payload) VALUES (1,1,$1)
            ON CONFLICT(id) DO UPDATE SET revision=lp_simulator_snapshot.revision+1,payload=EXCLUDED.payload
            WHERE lp_simulator_snapshot.revision=$2 RETURNING revision
            """, connection);
        cmd.Parameters.AddWithValue(snapshot); cmd.Parameters.AddWithValue(revision);
        var next = cmd.ExecuteScalar();
        if (next is null || (revision != 0 && (long)next != revision + 1)) throw new InvalidOperationException("Simulator snapshot revision changed.");
        revision = (long)next;
    }
    public void Dispose() => connection.Dispose();
}
