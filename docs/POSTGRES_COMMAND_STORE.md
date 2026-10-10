# PostgreSQL command delivery foundation

`LedgerPilot.Persistence.CommandStore` accepts an already-authorized canonical command and inserts its outbox row in a single transaction. Exact retries succeed; changed content under the same key fails. Receipts require an accepted command and reject changed results.

`PublishNext` holds a PostgreSQL row lock during the broker callback; competing publishers use `SKIP LOCKED`. A failed send rolls back, leaving the command pending. A crash after broker acceptance but before the database commit can send the same command again. This is at-least-once delivery: use the command key as Service Bus MessageId and recover SAP results by external command key. Broker duplicate detection alone is insufficient.

The caller must canonicalize payloads, authorize project access and bind approvals to current billing state before accepting commands. This library does not yet atomically transition billing state, implement an inbox execution lease, replace API JSON storage, connect to Service Bus or replace worker file receipts. `Complete` persists the result receipt; it does not make external SAP writes transactional. Do not deploy it as an end-to-end posting path yet.

Schema creation is currently an explicit development/test bootstrap. Versioned migrations, least-privilege database identities and Azure PostgreSQL Terraform are pending. Broker callbacks must have bounded timeouts so transactions do not hold locks indefinitely.

Run integration tests against a dedicated PostgreSQL database:

```sh
export POSTGRES_TEST_CONNECTION='Host=localhost;Username=ledgerpilot;Password=local-test-only;Database=ledgerpilot_tests'
dotnet test tests/LedgerPilot.Persistence.Tests -c Release
```

GitHub Actions provisions PostgreSQL 17 for these tests. Tests fail when the connection is absent rather than silently skipping. They exercise concurrent same-key acceptance, conflicting keys, broker failure/retry, competing publisher locks, durable receipts and unknown-command rejection. Local Release compilation passed; local PostgreSQL execution was unavailable in the restricted workspace. Check the commit's workflow run for integration execution evidence.
