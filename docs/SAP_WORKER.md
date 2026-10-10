# Local SAP integration worker

LedgerPilot.SapWorker is a separate one-pass process consuming *.command.json from SAP_COMMAND_PATH. It uses SAP_SIMULATOR_URL over local HTTP and writes an atomic *.result.json receipt only after reconciling a successful source result. DOTNET_ENVIRONMENT must be Development. The worker acquires exclusive directory ownership and validates unchanged commands against receipt hashes.

Create commands first query the simulator by external command key and verify project/net/currency/state before accepting a prior result. Post commands query the document and reconcile only when the saved posting command key and state match. Transport errors leave the command pending; rerunning the process retries/reconciles. A simulated source commit followed by HTTP 503 exercises the lost-response boundary without inventing success.

Example command: {"operation":"Create","commandKey":"demo-001","projectId":"SYNTHETIC-1","net":100,"currency":"GBP","stateHash":"demo-state"}. Post commands use operation Post, a new commandKey and the source documentId/stateHash.

Validated: new worker process after lost create/post response obtains the same source document/reference and saves one durable receipt. Repeating a completed command leaves the receipt unchanged. Simulator concurrent/restart tests also pass.

This local transport is not Azure Service Bus or a transactional API outbox. The billing API does not yet publish these commands. The worker does not independently validate human approval and must eventually consume only application-authorized outbox commands with authenticated service identity. Dead-letter policy, lock renewal, async hosting, PostgreSQL outbox/inbox and Durable Functions orchestration remain pending. HTTP failures are currently retained for manual retry; terminal/transient classification is not complete.

## Service Bus transport branch
Set SAP_TRANSPORT=ServiceBus, SERVICEBUS_NAMESPACE to the fully qualified namespace and SERVICEBUS_QUEUE (default sap-commands). Azure.Messaging.ServiceBus 7.21.0 and Azure.Identity 1.21.0 are pinned. DefaultAzureCredential authenticates without a committed connection string; the selected identity needs Azure Service Bus Data Receiver on the queue.

The processor uses one concurrent handler, explicit settlement and five-minute automatic lock renewal. MessageId must match CommandKey. It saves the canonical command to local spool, processes/reconciles it and completes only after a durable receipt exists. HTTP failure abandons for retry; malformed/changed/invalid command data is dead-lettered. Server-side max delivery count is configured by Terraform. SDK/IO failures are not completed automatically.

The branch compiles and the shared local reconciliation tests pass. No live queue receive/complete/abandon/dead-letter or lock-expiry test has run. Development-only worker and loopback simulator restrictions remain. A shared PostgreSQL inbox/receipt store must replace local spool before distributed replicas; Container Apps/identity deployment remains pending. Source reference: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/messaging.servicebus-readme

## PostgreSQL receipts and outbox dispatcher

Set `COMMAND_STORE_CONNECTION` on the worker to use PostgreSQL receipts. Service Bus mode now requires this setting. Each received command must match an existing accepted canonical payload; unknown or changed commands are rejected. Results are saved in PostgreSQL before completing the Service Bus message. Local file receipt mode remains available for isolated offline smoke tests.

`LedgerPilot.OutboxDispatcher` publishes PostgreSQL outbox entries to `SERVICEBUS_NAMESPACE` / `SERVICEBUS_QUEUE` using DefaultAzureCredential. MessageId is the immutable command key. Each dispatch attempt has a 30-second deadline; failed sends leave entries pending. Broker acceptance followed by a database commit failure can cause duplicate delivery, so SAP reconciliation remains necessary.

Development-only helpers (not approval endpoints):

```sh
dotnet run --project src/LedgerPilot.OutboxDispatcher -- --initialize
dotnet run --project src/LedgerPilot.OutboxDispatcher -- --enqueue command.json
dotnet run --project src/LedgerPilot.OutboxDispatcher -- --receipt COMMAND_KEY
dotnet run --project src/LedgerPilot.OutboxDispatcher
```

All require `DOTNET_ENVIRONMENT=Development` and a dedicated `COMMAND_STORE_CONNECTION`. Enqueue accepts synthetic test commands only through an explicit local CLI action. Do not expose these helpers as HTTP endpoints. Schema bootstrap should be replaced with deployment migrations.

The PostgreSQL smoke run tests lost-response create/post recovery, process restart, absence of file receipts, spool replacement and changed accepted-command rejection. GitHub CI runs it against PostgreSQL 17. Actual Azure queue delivery, distributed inbox execution leases, API approval-to-command transaction, hosted deployments and real SAP sandbox integration remain pending. Worker spool locking only protects one filesystem directory; this is not a distributed execution lock.
