# Copily / Strata Billing AI PoC — repository review and implementation plan
Date: 10 October 2026
Status: local foundation in progress; complete implementation and Azure deployment outstanding.

## Reviewed evidence
- Current Library copies: Strata Billing PoC - Rule Registry (1)(1).docx; Billing AI - Rules Checks and Controls(1).docx; deep-research-report(1).md.
- Materialized and inspected copily-azure-poc-starter.zip (Library version 1).
- Target repository is architectranbir/poc. Initial main contains README.md only. Work proceeds on feat/copily-foundation using the architectranbir account.
- The starter contains one ASP.NET Core .NET 10 API, React/Vite UI, five synthetic projects and 70 WIP lines, optional Blob reads/Cosmos writes, scripts and a coverage CSV.
- Python seed checks: 3 passed. They verify project uniqueness, basic WIP relationships/non-negative amounts and policy thresholds only.
- A local .NET 10 SDK and Node toolchain are now used. See VERIFICATION.md for current build/test evidence. Live Foundry and Azure execution remain unverified.
- The registry has 44 rules: W-01–10, A-01–04, V-01–12, P-01–18. Requirements also define 11 Step 1 checks (1.01–1.11); these are additional acceptance requirements.
- The registry's original input availability counts are 26 complete, 5 partial and 13 unavailable, with later 2 October clarifications. Those counts describe its supplied mock, not the starter's smaller dataset.

## Review findings
1. Adjustments are appended but never folded into remaining WIP or the PBD amount. W-08/V-01/P-01/P-07 cannot be credited.
2. Caller-provided Actor strings are trusted. There is no JWT validation or project authorization. Confirm=true is not authenticated human authorization.
3. CONFIRM findings do not block draft creation; Finance approval is computed in review only and does not protect submission.
4. Approval compares against the stored document hash, not a re-derived current billing state. Hash covers only project, amount and narrative, using culture-dependent formatting.
5. PBD/idempotency lookup uses process dictionaries. Optional Cosmos writes have no corresponding reads on recovery. Concurrent dictionary operations do not form an atomic document/key transaction.
6. Posting is an in-memory status change and fabricated reference, with no separate SAP adapter/worker, Service Bus or durable workflow.
7. AI receives client-supplied Facts. The endpoint variable named AZURE_OPENAI_ENDPOINT is passed to AIProjectClient. Verify the exact Foundry client/endpoint contract and package APIs through a compile-and-invoke spike.
8. Fixed price, VAT/FX, plans, history, accruals, acknowledgements and narrative checks remain absent or incomplete. Alternate billing currency is not applied to PBD creation.
9. UI auto-fills the requestor identity, sends confirm=true and offers separately triggered posting; intended final human approval should durably initiate posting.
10. No .NET solution/test project, lockfiles, GitHub Actions or complete infrastructure-as-code were found. Vite import.meta.env typings also require build verification.
11. Deployment script exposes external ingress before auth and assumes local seed availability despite the Docker image excluding seed files. Replace the script with reproducible infrastructure and authenticated deployment.
12. Coverage CSV marks zero rules complete. Partial source is useful scaffolding, not verified rule coverage.

## Architecture to implement
React/TypeScript with MSAL and Entra ID -> ASP.NET Core API on Container Apps.
API owns project authorization, typed commands and authoritative decision snapshots.
A C# domain library owns decimal arithmetic, event replay, validators, versioned policy and submission gates.
Microsoft Agent Framework in the API invokes a Microsoft Foundry model through narrowly scoped tools. Model never receives direct SAP credentials or bypass privileges.
Durable Functions (.NET isolated) owns long-running human waits, retry scheduling, reconciliation and workflow status.
Service Bus carries SAP commands/results; a worker calls a separately hosted simulated SAP API.
A transactional relational store holds Copily state, adjustments, findings, acknowledgements, approvals, outbox/inbox and idempotency outcomes.
The simulator has a separate logical data store and implements stable document IDs, lookup by external command key, versions and repeatable failure modes.
Azure Storage supports Durable state and synthetic fixture/artifact storage; Application Insights captures redacted correlation traces.

Use a modular monolith for API/domain. Keep workflow host, worker and simulator separate processes where execution boundaries matter.
Default persistence candidate: PostgreSQL to follow prior architecture; select the Azure SKU after actual trial eligibility and regional cost validation. An Azure SQL alternative requires an explicit documented architecture decision, not a silent switch.
Keep domain code independent of storage, Foundry and workflow SDKs.

## Contracts and invariants
- Money: decimal on server, explicit currency and scale, documented rounding policy. JSON money represented as exact decimal strings or integer minor units at browser boundaries.
- Time lines: quantity x rate; expenses: amount-only. Prefer validated SAP actual-total fields when present. Do not multiply expense amount by quantity.
- T&E amount derives from selected eligible lines, cutoff, committed adjustments and eligible accruals. Fixed-price amount derives from due milestones/schedule and remaining contract value.
- Persist immutable adjustment events with actor from token, UTC timestamp, reason, before/after allocation and expected state version.
- Canonical state hash includes billing scope, bill-to, line allocations, commercial context, accruals, relevant master data and policy versions; culture-independent serialization.
- Findings carry stable identity, rule ID, severity, dependencies and evidence. Acknowledgements bind to the finding's dependency hash; changed dependencies invalidate them.
- Approvals and human intents bind to exact draft state, document version, action and authenticated subject. Server issues/verifies receipts only following authorized UI actions; AI cannot issue them.
- Recheck entitlements and all gates at execution. Optimistic concurrency rejects conflicting edits.
- One transaction commits accepted command and outbox entry. Worker records inbox/result durably; simulator enforces unique external command key.
- At-least-once delivery plus idempotent processing/reconciliation produces one business effect. Do not claim a distributed exactly-once transaction.
- Lost SAP response: query simulator by external key before retry. Unknown outcomes remain reconciling, not blindly reposted.
- Final authorized PBD approval initiates posting durably. Finance approval is a separate prerequisite where thresholds require it.

## Policy and source discrepancies
1. A-01: registry wording uses proposed bill > £10,000 and diagnosis of an increase against prior bill; starter uses proposed minus available WIP > £10,000. Preserve both interpretations in an explicit decision record. Use configurable, labeled demo policy until business owner resolves the intended trigger.
2. V-03: permitted alternate-currency amount has no authoritative policy. Provide synthetic policy and negative fixtures; do not describe it as approved Finance policy.
3. V-06: 2 October clarification allows blank PoC narrative and optional project schedule text. AI draft is optional, character-limited, user committed and checked for any included authoritative figures. Do not require a pre-agreed wording source.
4. V-10: expenses are amount-only by design; use total-amount caps only if a policy exists. Per-night/person caps cannot be inferred.
5. P-15: registry lists requestor/primary manager/partner; clarification includes supporting roles per work package. Define explicit role policy and test it; named demo users are synthetic.
6. P-17: materiality is undefined. Demo defaults to invalidating approval for every financially or commercially relevant change; record this as an assumption.
7. Mandatory PO/reference is conditional in Step 1. Replace starter's unconditional missing-PO block with source-aligned policy.
8. Simulator supplies missing billing documents, prior-bill baseline, posting periods, accruals, approver directory, entity/org mappings and stable SAP line IDs. Mark each as synthetic and retain the real-SAP dependency tracker.

## Delivery sequence and gates
| Phase | Work | Exit evidence |
|---|---|---|
| 0 — access and baseline | Resolve GitHub repository, preserve starter, add review/decision records, establish toolchain and Azure target | Baseline commit, successful builds, access metadata |
| 1 — domain/data | Canonical 44-rule manifest, Step 1 checks, expanded fixtures, policy versions, pure validators and golden tests | Exact money reconciliation and expected outcomes for every deterministic rule |
| 2 — state/security | JWT/project RBAC, event fold, transactional store, dependency hashes, acknowledgement and approval records | Restart, concurrency, stale-state and authorization tests pass |
| 3 — full UI | Scope selection, WIP tables/filter preview, bulk adjustments, validations, Finance approval, PBD preview and audit timeline | Browser journeys pass against real API |
| 4 — simulator/workflow | Accrual/PBD/post/lookup simulator APIs, outbox/inbox, Service Bus worker, Durable human-event workflow | Duplicate delivery, lost response and worker restart yield one effect |
| 5 — Foundry/MAF | Verified SDK integration, server-sourced facts, typed filter tools, explanations, optional narrative and summary | Live-model evaluation and injection/authorization tests pass |
| 6 — Azure | Bicep, identities/RBAC, data store, Container Apps, Functions, Service Bus, Foundry configuration, observability and GitHub OIDC | Deployed authenticated smoke test with resource/deployment inventory |
| 7 — demonstration | All-rule scenario pack, recording/script, report and team setup guide | 44/44 registry coverage plus 11/11 Step 1 checks with inspectable evidence |

Milestone estimates should be set after the repository build spike, dataset inspection and Azure model quota check. No fixed completion date is justified yet.

## 44-rule implementation and acceptance matrix
All rows below are PLANNED, not completed. Tests must reference these IDs in machine-readable results.

| ID | Implementation | Required demonstration/test |
|---|---|---|
| W-01 | Exact totals per scope/WBS | Time vs amount-only expense totals |
| W-02 | Actual-person vs planned roster comparison | Unplanned person's time advisory |
| W-03 | Non-chargeable plan exception finding | Required acknowledgement before submission |
| W-04 | Actual vs planned effort by work package | Over-plan advisory with exact variance |
| W-05 | Actual vs planned expense/type | Unbudgeted expense advisory |
| W-06 | Confirmed bulk/single adjustment command | Preview and commit postpone/write-off |
| W-07 | AI typed filter parser; deterministic execution | Boris travel older than 90 days selects expected rows |
| W-08 | Replay adjustment event fold | Repeated edits and restart preserve remaining WIP |
| W-09 | Policy-driven age/repeated postponement | >180-day and repeated-postpone examples |
| W-10 | Immutable adjustment audit and rationale | Actor/time/before/after/reason/provenance inspectable |
| A-01 | Versioned accrual trigger and pending-item diagnosis | Both sides of £10,000 under labeled demo interpretation |
| A-02 | User-authorized optional accrual | Accept/decline; no extra approval imposed |
| A-03 | Durable idempotent simulator accrual command | Duplicate delivery creates one accrual |
| A-04 | Refresh source snapshot and revalidate | New accrual visible; dependent acknowledgements expire |
| V-01 | Derived proposed amount and cutoff | Amount changes from committed inputs only |
| V-02 | Explicit project/billing currencies | GBP/GBP and GBP/EUR examples |
| V-03 | Alternate amount policy and confirmation | Missing confirmation/invalid amount blocked |
| V-04 | Deterministic FX conversion metadata | Rate/source/date/equivalent shown and exact |
| V-05 | Master/policy VAT with acknowledgement | Standard/reverse charge/exempt examples |
| V-06 | Optional AI draft plus validator/user commitment | Incorrect included total rejected; blank PoC case permitted |
| V-07 | Conditional email/reference checks | Bad/missing email and required PO blocked |
| V-08 | Member-firm end client requirement | Missing fails; valid external/member-firm cases |
| V-09 | Receiving engagement requirement | Missing fails; valid code passes |
| V-10 | Time rate and available total-expense caps | Grade-cap breach; missing policy visibly unavailable |
| V-11 | Fixed-price schedule/milestone/contract checks | Due/open/billed/not-due milestones and contract overflow |
| V-12 | Decimal reconciliation/rounding/VAT | Bill + postpone + write-off reconciles each source line |
| P-01 | Write-off from committed event fold | Exactly £10,000 seed and partial allocations |
| P-02 | Explicit authorized write-up vs billing basis | £22,000 write-up, independently calculated |
| P-03 | Tax & Legal Finance gate >£10,000 | Above threshold waits for authorized Finance approval |
| P-04 | Advisory Finance gate >£20,000 | Write-off or write-up independently triggers |
| P-05 | Audit Finance gate >£10,000 | Approve/reject with evidence |
| P-06 | Policy strict-greater comparator | £9,999.99/£10,000/£10,000.01 and £20,000 equivalents |
| P-07 | Revenue effect from same authoritative fold | Reconciles with remaining WIP and derived bill |
| P-08 | Cost/margin calculation with availability | Missing cost says unavailable, never zero |
| P-09 | Submission gate lists unresolved findings | BLOCK and unacknowledged CONFIRM prevent draft |
| P-10 | Dependency-driven revalidation | Relevant edits expire acknowledgements; unrelated edits tested |
| P-11 | Summary from injected validated facts | No invented amount/currency/revenue/margin |
| P-12 | Authenticated exact-state human intent | No intent or stale intent rejects draft request |
| P-13 | Durable PBR/BDR/PBD creation adapter | Simulator-created references and one business effect |
| P-14 | Retrieve authoritative simulator PBD | Version, content, amount and status match source |
| P-15 | Project/work-package role authorization | Valid mapped roles pass; unrelated user denied |
| P-16 | Signed exact-version approval starts posting | One posted document after durable approval |
| P-17 | Material edit invalidates prior authorization | Draft changed after creation/approval requires revalidation |
| P-18 | Persistent idempotency/reconciliation | Session retry, concurrent delivery and lost response produce one post |

## Additional Step 1 acceptance
Implement 1.01 valid project; 1.02 authorized role; 1.03 eligible billing elements/type; 1.04 explicit element selection; 1.05 T&E WIP availability; 1.06 multiple bill-to selection; 1.07 element/party compatibility; 1.08 external/member-firm classification; 1.09 billing email confirmation/correction; 1.10 conditional PO/reference confirmation; 1.11 existing/overlapping draft warning and acknowledgement.
Posting period checks from the wider requirements are also tracked beyond the 44 IDs.

## Proposed source layout
- web/ — React, MSAL, typed API client, Playwright tests
- src/Copily.Domain/ — pure rules, money, policies, state/dependency hash
- src/Copily.Application/ — commands, gates and use cases
- src/Copily.Api/ — authenticated HTTP endpoints and MAF tools
- src/Copily.Infrastructure/ — persistence/outbox/inbox/provider adapters
- src/Copily.Workflows/ — Durable Functions isolated host
- src/Copily.SapWorker/ — Service Bus consumer/reconciliation
- src/Copily.SapSimulator/ — synthetic SAP contracts and financial document store
- tests/ — domain, contract, integration, workflow, authorization, AI evaluations
- seed/ — complete versioned fixtures and expected outcomes
- infra/ — Bicep modules, parameter examples, deploy/cleanup scripts
- docs/ — decisions, rule manifest, demo and evidence
- .github/workflows/ — build/test, deployment with OIDC, Azure smoke/evidence

## Automated testing and completion criteria
- Unit/property tests: exact arithmetic, allocations, VAT/FX, fixed price, policy boundaries and all findings.
- API/integration tests: authoritative actor, project isolation, validation gates, persistence and optimistic concurrency.
- Contract tests: simulator accrual, document create/retrieve/approve/post and idempotency lookup.
- Workflow tests: human approval/rejection/timeout, duplicate events, stale approvals and resume after restart.
- Fault injection: crash before dispatch, after source commit before response, before local result save; expired locks, duplicate delivery, poison message/dead letter and reconciliation.
- Playwright: authenticated positive/negative journeys. Test-only identities must never be enabled accidentally in Azure.
- Live Foundry evaluations: paraphrases, typed predicate semantics, malicious narratives, numeric fidelity and unavailable-model behavior.
- CI blocks merge on failed deterministic tests and missing IDs. Publishing model evaluation results distinguishes real model runs from offline fixtures.
- Coverage completeness means every rule has a positive case, applicable negative/boundary cases and evidence at its owning layer. A helper labeled with a rule ID is not sufficient.
- Final report records commit SHA, deployed resource versions, policy/fixture/model versions, test counts and failures, rule-level status, simulator effects and actual costs.

## Azure Free Trial deployment plan
Treat Free Trial as an eligible funding/subscription offer, not a guarantee that every chosen service is free.
Check actual subscription offer, expiry/remaining credit, supported regions, permissions, resource provider registration, model availability/quota and Entra app registration before provisioning.
Use a dedicated PoC resource group, low-scale Container Apps configuration, consumption Functions, Service Bus Standard candidate, appropriately sized durable data store and one economical Foundry model.
Verify exact service feature/tier requirements in the target subscription rather than relying on historical free allowances.
Apply maximum instance limits, AI token/request quotas, trace sampling/retention, budget notifications and documented shutdown/cleanup. Budget notifications are not a hard spending cap.
Provision Entra auth and managed identity/RBAC before exposing write-enabled endpoints. GitHub deployment uses federated OIDC, no committed credentials.
First deploy deterministic stack and simulator; then enable live model and cloud workflow tests.
Artifact/build registries, logging, database and AI inference may consume credit. Record actual deployment estimate from current regional pricing before execution.
No Azure subscription/session has been supplied or verified, and this environment has no Azure CLI. Cloud deployment is presently an access dependency.

## End-to-end demo script
1. Sign in as synthetic requestor; select project, WBS and bill-to.
2. Review WIP/plan/history; natural-language filter preview; confirm bulk postponement/write-off.
3. Diagnose pending expenses/invoices; explicitly authorize accrual; observe simulator reference and refreshed validations.
4. Demonstrate alternate currency/VAT, narrative correction, member-firm missing-field block, rate cap and fixed-price schedule cases.
5. Show exact-boundary Finance exemption and above-threshold Finance approval/rejection.
6. Resolve mandatory confirmations, show final validated summary, explicitly request PBD.
7. Retrieve authoritative simulator PBD; approve as authorized project role; workflow posts and returns reference.
8. Retry instruction/message; verify unchanged simulator effect count.
9. Edit after approval, deny unauthorized user, lose source response and restart worker; show safe rejection/reconciliation.
10. Display rule coverage evidence for all 44 IDs and additional Step 1 checks.

The demo is a suite of journeys across fixture projects, not a claim that one successful bill exercises every rule.

## Inputs needed to proceed with external integration
- GitHub target/access resolved: architectranbir/poc, signed in as architectranbir with push permission.
- Azure tenant/subscription target, preferred region and available authenticated deployment route. Do not send passwords, private keys or client secrets in chat.
- Original synthetic workbook/extract/generator if the 151-line registry dataset is available. Until then build explicitly synthetic fixtures; do not claim exact fidelity to the unavailable dataset.
Business-policy ambiguities do not prevent simulator development; their assumptions remain labeled and configurable.

## Official implementation references checked
- https://learn.microsoft.com/en-us/agent-framework/overview
- https://learn.microsoft.com/en-us/azure/durable-task/durable-functions/durable-functions-dotnet-isolated-overview
- https://azure.microsoft.com/en-in/pricing/details/service-bus/
- https://azure.microsoft.com/en-in/pricing/free-services/

Current SDK/client signatures, package versions and regional eligibility must be checked during the build/deployment spike. Recommendations in the Deep Research report are input evidence, not proof that the starter's package/version choices compile.

