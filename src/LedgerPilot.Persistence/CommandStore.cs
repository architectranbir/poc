using Npgsql;

namespace LedgerPilot.Persistence;

// Store one canonical, already-authorized command per key. No SAP calls occur in transactions.
public sealed class CommandStore(NpgsqlDataSource source)
{
    public async Task Initialize(CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS lp_command (
              command_key text PRIMARY KEY, payload text NOT NULL,
              accepted_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS lp_outbox (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              published_at timestamptz);
            CREATE TABLE IF NOT EXISTS lp_receipt (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              result text NOT NULL, completed_at timestamptz NOT NULL DEFAULT now());
            """);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task Accept(string key, string canonicalPayload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPayload);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var insert = new NpgsqlCommand("INSERT INTO lp_command(command_key,payload) VALUES ($1,$2) ON CONFLICT DO NOTHING", connection, tx);
        insert.Parameters.AddWithValue(key); insert.Parameters.AddWithValue(canonicalPayload);
        await insert.ExecuteNonQueryAsync(ct);
        await using var check = new NpgsqlCommand("SELECT payload FROM lp_command WHERE command_key=$1 FOR UPDATE", connection, tx);
        check.Parameters.AddWithValue(key);
        if ((string?)await check.ExecuteScalarAsync(ct) != canonicalPayload)
            throw new InvalidOperationException("Command key was reused with different content.");
        await using var outbox = new NpgsqlCommand("INSERT INTO lp_outbox(command_key) VALUES ($1) ON CONFLICT DO NOTHING", connection, tx);
        outbox.Parameters.AddWithValue(key); await outbox.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    // The lock spans the broker send. A crash after sending may redeliver the same key;
    // consumers must use durable receipts and SAP's external command key for recovery.
    public async Task<bool> PublishNext(Func<string, string, CancellationToken, Task> send, CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var claim = new NpgsqlCommand("""
            SELECT o.command_key,c.payload FROM lp_outbox o JOIN lp_command c USING(command_key)
            WHERE o.published_at IS NULL ORDER BY c.accepted_at,o.command_key
            FOR UPDATE OF o SKIP LOCKED LIMIT 1
            """, connection, tx);
        string key, payload;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            key = reader.GetString(0); payload = reader.GetString(1);
        }
        await send(key, payload, ct);
        await using var mark = new NpgsqlCommand("UPDATE lp_outbox SET published_at=now() WHERE command_key=$1", connection, tx);
        mark.Parameters.AddWithValue(key); await mark.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task Complete(string key, string result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var insert = new NpgsqlCommand("INSERT INTO lp_receipt(command_key,result) VALUES ($1,$2) ON CONFLICT DO NOTHING", connection, tx);
        insert.Parameters.AddWithValue(key); insert.Parameters.AddWithValue(result);
        await insert.ExecuteNonQueryAsync(ct);
        await using var check = new NpgsqlCommand("SELECT result FROM lp_receipt WHERE command_key=$1 FOR UPDATE", connection, tx);
        check.Parameters.AddWithValue(key);
        if ((string?)await check.ExecuteScalarAsync(ct) != result)
            throw new InvalidOperationException("Completed command has a different result.");
        await tx.CommitAsync(ct);
    }

    public async Task VerifyAccepted(string key, string canonicalPayload, CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("SELECT payload FROM lp_command WHERE command_key=$1");
        cmd.Parameters.AddWithValue(key);
        if ((string?)await cmd.ExecuteScalarAsync(ct) != canonicalPayload)
            throw new InvalidDataException("Command is not accepted or its content differs.");
    }

    public async Task<string?> Receipt(string key, CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("SELECT result FROM lp_receipt WHERE command_key=$1");
        cmd.Parameters.AddWithValue(key);
        return (string?)await cmd.ExecuteScalarAsync(ct);
    }
}
