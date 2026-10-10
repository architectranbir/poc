# Local SAP integration worker

LedgerPilot.SapWorker is a separate one-pass process consuming *.command.json from SAP_COMMAND_PATH. It uses SAP_SIMULATOR_URL over local HTTP and writes an atomic *.result.json receipt only after reconciling a successful source result. DOTNET_ENVIRONMENT must be Development. The worker acquires exclusive directory ownership and validates unchanged commands against receipt hashes.

Create commands first query the simulator by external command key and verify project/net/currency/state before accepting a prior result. Post commands query the document and reconcile only when the saved posting command key and state match. Transport errors leave the command pending; rerunning the process retries/reconciles. A simulated source commit followed by HTTP 503 exercises the lost-response boundary without inventing success.

Example command: {"operation":"Create","commandKey":"demo-001","projectId":"SYNTHETIC-1","net":100,"currency":"GBP","stateHash":"demo-state"}. Post commands use operation Post, a new commandKey and the source documentId/stateHash.

Validated: new worker process after lost create/post response obtains the same source document/reference and saves one durable receipt. Repeating a completed command leaves the receipt unchanged. Simulator concurrent/restart tests also pass.

This local transport is not Azure Service Bus or a transactional API outbox. The billing API does not yet publish these commands. The worker does not independently validate human approval and must eventually consume only application-authorized outbox commands with authenticated service identity. Dead-letter policy, lock renewal, async hosting, PostgreSQL outbox/inbox and Durable Functions orchestration remain pending. HTTP failures are currently retained for manual retry; terminal/transient classification is not complete.
