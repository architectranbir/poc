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
