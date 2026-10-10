# Implementation truth table

| Item | Status | Notes |
|---|---|---|
| Synthetic project / WIP data | Implemented / seed tested | Five projects, 70 WIP lines |
| C# API and rule subset | Release build and local API smoke pass | See VERIFICATION.md |
| Domain money/replay/threshold functions | 14 C# tests pass | API totals/thresholds use domain; full persisted adjustment flow is still pending |
| Agent Framework / Foundry | Compiled with pinned SDK; live inference untested | Requires Foundry project endpoint, model and Azure identity |
| React UI | Production build passes; browser testing pending | React + Vite with lockfile |
| Mock PBD create/approve/post | Demo code supplied | State is process-memory with optional Cosmos writes; not restart safe |
| Full 44 rules | NOT implemented | Prioritised follow-on work |
| Durable Functions | NOT implemented | Deferred for workflow integration |
| Service Bus | NOT implemented | Deferred for SAP worker |
| Azure Entra end-user authentication | NOT implemented | No production access-control claim |
| SAP integration | NOT implemented | Deliberately simulated |
| All-service Azure end-to-end tests | NOT performed | Needs account access and deployment |

The API rejects non-Development startup and non-loopback requests. The deployment script is disabled.
State-changing endpoints still lack end-user authentication and are a local synthetic demonstration only.
The financial document's full controls are not yet implemented.

## Submission control domain slice
The pure SubmissionGate now rejects blocking findings, confirmations with stale dependency hashes, missing or stale exact-state human intent, and required Finance approvals with an incorrect state or role. An unrelated edit can preserve an acknowledgement when its finding dependencies remain unchanged. Ten additional cases verify these decisions (24 domain tests total).

These are domain controls, not completed authenticated API workflows. The current API has not yet integrated this gate. Actor and role inputs must come from validated identity and persisted authorization records before shared deployment. P-09/P-10/P-12/P-17 remain partial until API, persistence and browser acceptance tests pass.

## API adjustment replay integration
Committed partial adjustments now fold into WIP, review availability and newly created PBD amounts. Adjustment requests require a positive amount, rationale and stable event ID. Identical retries have one in-process effect; conflicting event reuse and allocation overflow return HTTP 400. Approval and posting rederive the financial snapshot and reject a changed draft with HTTP 409.

The local smoke test verifies those behaviors against the compiled API. This is still an in-memory, loopback-only development implementation. It does not prove persistence across restart, full commercial-state hashing, Finance gating, authenticated authorization or transactional posting. Full submission-gate integration remains pending.

## Local restart recovery
The development store now writes versioned atomic JSON snapshots for adjustments, drafts, draft idempotency keys, approvals and simulated posting references. Flush and file replacement occur before returning success; save failures restore the prior in-memory state. Startup loads the snapshot and acquires an exclusive process lock before listening. Invalid JSON, unsupported schema and broken draft-key references fail startup.

Smoke coverage stops/restarts the API, verifies adjusted totals, repeats an adjustment and draft command, retrieves the same posting reference, rejects stale draft posting, checks exclusive store ownership and rejects corrupted JSON.

This is a single-process local recovery adapter, not the Azure transactional relational store. It does not provide distributed concurrency, database transactions/outbox, disaster recovery or power-loss guarantees across filesystems. Hosted deployment remains disabled. COSMOS_ENDPOINT is rejected because the earlier write-only Cosmos path could not recover authoritative state. STATE_PATH selects an isolated development file; never use production financial data.
