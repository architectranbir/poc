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

## API snapshot and atomic posting acceptance

The API now selects PostgreSQL billing state when `COMMAND_STORE_CONNECTION` is supplied. A single JSON snapshot in `lp_billing_snapshot` retains adjustments, audit snapshots, write-ups, Finance approvals, PBDs and idempotency keys. A session advisory lock rejects a second API writer; revision checks guard snapshot writes. This deliberately supports one API instance only. Project/WIP master data still comes from the configured seed/blob source. Existing local JSON files are not automatically imported.

Confirmed posting atomically saves `POST_PENDING`, the immutable `CreateAndPost` command and its outbox row. The worker creates/reconciles the simulator document, posts/reconciles it, then saves its receipt. API GET PBD reconciles that receipt to `POSTED`. The web UI includes an explicit refresh control. Approval and authorization precede acceptance; the billing hash is checked again under the API mutation lock.

GitHub's PostgreSQL API smoke uses a synthetic local file handoff of the exact accepted command to the worker, which verifies it against PostgreSQL. It does not run the Azure Service Bus transport. Both create and post responses are deliberately lost after simulator commit; retries recover the same document and posting reference. API restart, PostgreSQL-only state, duplicate posting requests and second-writer rejection are also checked.

This is a conservative single-writer PoC migration, not normalized multi-instance billing storage. Schema bootstrap, synchronous snapshot persistence and the legacy `MockSapPostingId` response field remain temporary. Distributed inbox leases, versioned migrations, live Service Bus validation, Durable Functions orchestration and Azure deployment remain pending. After an ambiguous database commit or connection failure, restart the API to reload committed state before resuming operations.

## Worker inbox ownership

`lp_inbox` records a UUID owner and a database-clock expiry for each accepted command. `Claim` locks the accepted command while checking its canonical payload, existing receipt and ownership. Concurrent claim attempts produce one owner. Completed deliveries return the stored receipt without claiming execution.

The worker uses a 45-second execution deadline within a 120-second lease. `CompleteOwned` checks the unexpired ownership token in the same transaction as receipt persistence. Replaced or expired owners cannot save a receipt. Normal failures release ownership with a token-qualified update; a crashed worker's lease expires. Broker messages are completed only after a receipt exists. Busy claims are abandoned for redelivery; this can consume Service Bus delivery attempts, so live load tests must validate backoff and dead-letter recovery before deployment.

A lease does not make external SAP writes exactly once. Process suspension, network partitions or an HTTP request accepted after cancellation can overlap an expired worker with its replacement. The stable external SAP command key and reconciliation remain mandatory. No lease renewal is needed for the current bounded synthetic operation; longer real SAP workflows need reviewed timing/renewal or orchestration. The older `Complete` method is for isolated receipt tests; runtime workers use only `CompleteOwned`.

Three additional PostgreSQL tests cover eight competing claims, receipt replay, wrong-owner rejection, expiry takeover, stale-owner commit/release rejection and immediate retry after release. Existing lost-response and API-to-worker tests exercise worker ownership along with SAP reconciliation.
