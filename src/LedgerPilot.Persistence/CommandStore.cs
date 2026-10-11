using Npgsql;

namespace LedgerPilot.Persistence;

// Store one canonical, already-authorized command per key. No SAP calls occur in transactions.
public sealed class CommandStore(NpgsqlDataSource source)
{
    public async Task Initialize(CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // API, worker, dispatcher and Functions may boot together. Serialize additive
        // schema creation; IF NOT EXISTS alone can still race in PostgreSQL catalogs.
        await using var migrationLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(73010453820)", connection, tx);
        await migrationLock.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS lp_command (
              command_key text PRIMARY KEY, payload text NOT NULL,
              accepted_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS lp_outbox (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              published_at timestamptz);
            CREATE TABLE IF NOT EXISTS lp_inbox (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              owner uuid NOT NULL, lease_until timestamptz NOT NULL);
            CREATE TABLE IF NOT EXISTS lp_receipt (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              result text NOT NULL, completed_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS lp_workflow_tracking (
              command_key text PRIMARY KEY REFERENCES lp_command(command_key),
              scheduled_at timestamptz NOT NULL DEFAULT now());
            """, connection, tx);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
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

    public async Task<ExecutionClaim> Claim(string key, string payload, CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await LockAccepted(connection, tx, key, payload, ct);
        await using var receipt = new NpgsqlCommand("SELECT result FROM lp_receipt WHERE command_key=$1", connection, tx);
        receipt.Parameters.AddWithValue(key);
        if (await receipt.ExecuteScalarAsync(ct) is string result) return new(null, result);
        var owner = Guid.NewGuid();
        await using var claim = new NpgsqlCommand("""
            INSERT INTO lp_inbox(command_key,owner,lease_until) VALUES ($1,$2,clock_timestamp()+interval '120 seconds')
            ON CONFLICT(command_key) DO UPDATE SET owner=EXCLUDED.owner,lease_until=EXCLUDED.lease_until
            WHERE lp_inbox.lease_until<=clock_timestamp() RETURNING owner
            """, connection, tx);
        claim.Parameters.AddWithValue(key); claim.Parameters.AddWithValue(owner);
        var acquired = await claim.ExecuteScalarAsync(ct) is Guid;
        await tx.CommitAsync(ct);
        return new(acquired ? owner : null, null);
    }

    public async Task CompleteOwned(string key, Guid owner, string result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Same command row lock as Claim: claim replacement cannot race receipt commit.
        await using var command = new NpgsqlCommand("SELECT command_key FROM lp_command WHERE command_key=$1 FOR UPDATE", connection, tx);
        command.Parameters.AddWithValue(key); await command.ExecuteScalarAsync(ct);
        await using var lease = new NpgsqlCommand("SELECT 1 FROM lp_inbox WHERE command_key=$1 AND owner=$2 AND lease_until>clock_timestamp() FOR UPDATE", connection, tx);
        lease.Parameters.AddWithValue(key); lease.Parameters.AddWithValue(owner);
        if (await lease.ExecuteScalarAsync(ct) is null) throw new ExecutionLeaseLostException();
        await using var insert = new NpgsqlCommand("INSERT INTO lp_receipt(command_key,result) VALUES ($1,$2) ON CONFLICT DO NOTHING", connection, tx);
        insert.Parameters.AddWithValue(key); insert.Parameters.AddWithValue(result); await insert.ExecuteNonQueryAsync(ct);
        await using var check = new NpgsqlCommand("SELECT result FROM lp_receipt WHERE command_key=$1", connection, tx);
        check.Parameters.AddWithValue(key);
        if ((string?)await check.ExecuteScalarAsync(ct) != result) throw new InvalidDataException("Conflicting command receipt.");
        await tx.CommitAsync(ct);
    }

    public async Task Release(string key, Guid owner, CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("UPDATE lp_inbox SET lease_until=clock_timestamp() WHERE command_key=$1 AND owner=$2");
        cmd.Parameters.AddWithValue(key); cmd.Parameters.AddWithValue(owner);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task LockAccepted(NpgsqlConnection connection, NpgsqlTransaction tx, string key, string payload, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT payload FROM lp_command WHERE command_key=$1 FOR UPDATE", connection, tx);
        cmd.Parameters.AddWithValue(key);
        if ((string?)await cmd.ExecuteScalarAsync(ct) != payload) throw new InvalidDataException("Command is not accepted or its content differs.");
    }

    public async Task<string?> AcceptedPayload(string key, CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("SELECT payload FROM lp_command WHERE command_key=$1");
        cmd.Parameters.AddWithValue(key);return (string?)await cmd.ExecuteScalarAsync(ct);
    }

    // The offline SAP worker must still discover only work without a receipt.
    // Its execution queue is independent of the Durable scheduling marker.
    public async Task<string[]> PendingPostings(CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("""
            SELECT c.command_key,c.payload FROM lp_command c LEFT JOIN lp_receipt r USING(command_key)
            WHERE r.command_key IS NULL AND
              CASE WHEN pg_input_is_valid(c.payload, 'jsonb')
                THEN c.payload::jsonb->>'Operation' END = 'CreateAndPost'
            ORDER BY c.accepted_at,c.command_key LIMIT 1000
            """);
        return await ReadPostingKeys(cmd, ct);
    }

    public async Task<string[]> UntrackedPostings(CancellationToken ct = default)
    {
        // Receipt arrival must not hide a command from the timer. Exclude only commands
        // whose deterministic Durable instance has already been scheduled or observed.
        // Filter non-posting fixtures before the batch limit so they cannot starve discovery.
        await using var cmd = source.CreateCommand("""
            SELECT c.command_key,c.payload FROM lp_command c
            LEFT JOIN lp_workflow_tracking w USING(command_key)
            WHERE w.command_key IS NULL AND
              CASE WHEN pg_input_is_valid(c.payload, 'jsonb')
                THEN c.payload::jsonb->>'Operation' END = 'CreateAndPost'
            ORDER BY c.accepted_at,c.command_key LIMIT 1000
            """);
        return await ReadPostingKeys(cmd, ct);
    }

    static async Task<string[]> ReadPostingKeys(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);var keys = new List<string>();
        while (await reader.ReadAsync(ct)) {
            try {
                var command = System.Text.Json.JsonSerializer.Deserialize<SapCommand>(reader.GetString(1));
                if (command?.Operation == "CreateAndPost" && command.CommandKey == reader.GetString(0)) {
                    command.Validate();keys.Add(command.CommandKey);
                }
            } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { }
        }
        return keys.ToArray();
    }

    // Call only after Durable confirms scheduling or an existing stable instance ID.
    // A crash before this insert rediscovers the command and checks that same ID.
    public async Task MarkPostingTracked(string key, CancellationToken ct = default)
    {
        await using var cmd = source.CreateCommand("INSERT INTO lp_workflow_tracking(command_key) VALUES ($1) ON CONFLICT DO NOTHING");
        cmd.Parameters.AddWithValue(key);
        await cmd.ExecuteNonQueryAsync(ct);
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

public sealed record ExecutionClaim(Guid? Owner, string? Result);
public sealed class ExecutionLeaseLostException : Exception { public ExecutionLeaseLostException() : base("Execution lease expired or changed.") { } }
