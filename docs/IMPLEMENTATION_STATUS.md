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

## Authoritative AI inputs and demo role checks
The explain endpoint now loads project, financial position and findings server-side. Caller-supplied facts are rejected, unknown projects return 404, and empty/oversized messages return 400. Decimal figures are serialized as invariant exact strings in the model context. The React caller no longer supplies facts. This does not guarantee numeric fidelity in generated text; live-model evaluations remain pending.

Adjustment, draft creation, approval and posting (including repeated posting requests) consistently require a project-mapped demo role. Unrelated actor strings are rejected with 403. These are synthetic local role checks, not authentication: actor strings remain caller-supplied, so JWT validation/project authorization must replace them before shared use. Loopback and Development restrictions remain enforced.

Compiled smoke checks cover unrelated adjustment/draft/post actors and rejected AI fact injection, missing project and message length boundaries. No live Foundry inference was performed.

## Confirmation finding gate
W-03 confirmation findings now carry a stable line-based identity and a canonical dependency hash over line identity, person and planned chargeability. Draft creation rejects missing or stale acknowledgements and retains the accepted IDs/hashes with the draft snapshot. React exposes explicit per-finding review checkboxes and clears selection when switching projects. No automatic confirmation is submitted.

Compiled API smoke requires a fixture with confirmation findings and verifies missing/stale rejection and current acceptance; React build passes. Browser interaction tests remain pending. This is the W-03 acknowledgement slice only: Finance approval, complete validator dependencies, authenticated intent receipts and all-rule submission integration remain outstanding.

## Finance approval integration
Draft creation derives Finance requirements from committed adjustments, using the strict unit-specific domain threshold. The separate Finance endpoint validates the mapped synthetic Finance actor, explicit confirmation and current financial hash. Approval is saved in the local recovery snapshot; subsequent financial edits make the old hash unusable. The React page exposes a clearly labeled demo Finance action.

Compiled smoke exercises exact threshold exemption, threshold plus £0.01, missing approval, incorrect role, stale hash, successful approval/draft and invalidation after another adjustment. Release/React builds pass. This verifies the write-off API slice; write-up commands, rejection/timeout workflows, authenticated Finance identity, complete commercial hashing and browser tests remain pending. DemoFinance is a synthetic persona, not a real account.

## Entra API authentication foundation
JWT bearer validation and a delegated tenant/scope policy now protect all API routes in default Entra mode. Mutation identity derives from oid; project reads are filtered/checked by the mapped identity. LocalDemo requires explicit mode selection and retains loopback/Development constraints. Locked restore and Release build pass; compiled smoke rejects anonymous read/AI/write calls and missing Entra configuration. See ENTRA_SETUP.md for remaining app registration, identity mapping, signed-token and browser tests. This does not make shared deployment ready.

## React Microsoft sign-in
React now initializes MSAL, presents an explicit Microsoft account selection popup, acquires API tokens silently and supplies bearer headers. Project loading waits for sign-in. Sign-out clears displayed billing state. A dedicated redirect bridge is included as a separate Vite build entry. The dependency and npm lockfile pin MSAL 5.25.0.

Default and Entra-configured production builds were verified; the configured build uses synthetic registration identifiers solely to check compilation/bundling. No real authentication was performed. LocalDemo is explicitly limited to localhost Vite development, with no implicit production fallback. Missing configuration displays a setup message. App registrations, real login/token validation, project object-ID mapping and browser journey tests remain pending.

## Committed write-ups
Write-up commands now validate project role, confirmation, positive amount, rationale and stable event ID. Events are saved in the local state snapshot and folded into bill/revenue figures and financial hashes. Identical retries return the existing event; conflicting reuse returns 400. Review and draft Finance checks use committed write-off/write-up values, not caller review amounts. UI retries reuse a command ID for unchanged input.

API smoke verifies a £22,000 uplift, single effect on retry, invalid/unauthorized/conflicting rejection, revenue reconciliation and fresh Finance approval before draft creation. .NET and React builds pass. Real browser tests, restart-specific write-up test and Azure transactional storage remain pending. Currency/VAT/fixed-price rules and authenticated external demonstration are not complete.

## Currency/VAT calculation preview
Pure decimal calculations now validate FX pair/source/positive rate, same-currency identity rate, VAT treatment/rate and nonnegative net. Converted net and VAT are rounded under the documented money policy; gross reconciles as net plus VAT. Twelve tests cover standard/exempt/reverse-charge, conversion, midpoint and rejected policies (36 domain tests total).

The API derives net from committed state and exposes a synthetic dated rate/source and VAT preview. React displays the preview with an explicit draft-integration limitation. API/React builds and preview smoke pass. PBD currently remains project-currency net: commercial-state persistence/hash, alternate amount confirmation and VAT acknowledgement are pending. Do not mark V-02/V-04/V-05/V-12 complete from preview helpers alone.

## Commercial draft integration
Drafts now save the converted net, billing currency, VAT/gross, FX metadata and narrative. VAT acknowledgement is mandatory; alternate-currency creation also requires explicit currency confirmation. Finance state hashes include current commercial policy/master fields, while draft hashes additionally bind the saved narrative. Approval/posting rederive this state. Earlier draft hashes fail revalidation and require a new draft.

Smoke verifies GBP/VAT reconciliation, VAT acknowledgement rejection, EUR currency confirmation, synthetic FX/reverse-charge result and changed billing-email rejection using isolated seed copies. Builds and smoke pass. This extends the supported commercial slice, not every dependency: alternate manually specified amounts, commercial edits via authorized UI, complete finding revalidation and external simulator contracts remain pending. The rate remains synthetic.

## Fixed-price schedule validation
A separate fixed-price calculator validates contract/history values, unique milestone identity, due cutoff, cancellation, partially/fully billed amounts and remaining contract capacity. It derives the proposed bill solely from selected outstanding milestone values. Eleven new domain cases pass (47 total).

The API preview reads a server-side synthetic schedule; callers supply only cutoff and milestone selection. Smoke verifies the due £20,000 milestone and rejects already billed, future, unknown and duplicate selections. The fixture explicitly separates past/due/future milestones. Fixed-price draft selection, schedule concurrency, posting updates and UI integration remain pending; V-11 remains partial.
