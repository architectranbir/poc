# Foundation verification — 10 October 2026

Locally verified:

- .NET SDK 10.0.401: Release solution build, zero warnings/errors.
- React/Vite: production build passes with committed dependency lockfile.
- xUnit: 14 domain tests pass.
- Python: 3 seed checks pass.
- Local API smoke: health, project data, unknown-project rejection, missing confirmation
  rejection, draft retry identity, approval-required posting, unrelated-approver rejection,
  approve/post and repeated-post identity all pass.
- AI-disabled mode explicitly reports missing configuration.
- Production environment startup is rejected.

The smoke script starts the actual compiled ASP.NET Core application.
It exercises the inherited limited simulation, not the complete financial submission gate.
Domain tests verify amount-only expenses, time multiplication, event replay/reconciliation,
duplicate event handling, over-allocation rejection, strict thresholds, rounding and
culture/order-independent financial hashes.

Not yet verified or implemented:

- All 44 rules and 11 additional Step 1 checks.
- Authentication, project RBAC and exact-state human approvals.
- Persisted event replay through the API or restart-safe document state.
- Durable Functions, Service Bus, separate SAP simulator and transactional outbox.
- Browser end-to-end tests, live Foundry inference and model evaluation.
- Azure infrastructure, deployment, cloud smoke tests and cost measurement.

GitHub Actions runs restore/build/domain/seed/API smoke/frontend checks.
Its first run must be inspected after pushing; local success does not prove hosted CI success.
