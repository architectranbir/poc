# LedgerPilot — Azure PoC — development foundation

A development foundation for the LedgerPilot PoC. It is NOT a certified, fully tested 44-rule implementation, real SAP integration, or production-ready financial application. Read `docs/IMPLEMENTATION_STATUS.md` before deployment.

## Architecture

React / Static Web Apps → ASP.NET Core (.NET 10) / Container Apps → C# rules + Microsoft Agent Framework → Foundry model; seed in Azure Blob (or local files); optional Cosmos persistence; mock SAP adapter. Durable Functions and Service Bus are **planned but not included in code in this baseline**.

## Local prerequisites

- .NET 10 SDK
- Node.js 22+
- Azure CLI and Azure account (for cloud setup)
- For AI calls: Foundry model deployment, endpoint, and `az login`; assign model invoke permissions.

## Local run

1. Terminal A: `bash scripts/run-local.sh` (API on http://localhost:8080, local Development mode)
2. Terminal B: `cd web && npm install && npm run dev` (React on http://localhost:5173)
3. `python3 -m unittest discover -s tests` verifies the sample data.
4. `curl http://localhost:8080/health`
5. `curl http://localhost:8080/api/projects`
6. `curl http://localhost:8080/api/projects/CU10453820P01/wip`

Dependency lockfiles pin the verified restore graph. See docs/VERIFICATION.md for build/test evidence.

## Build and test

```bash
dotnet restore LedgerPilot.slnx --locked-mode
dotnet build LedgerPilot.slnx -c Release --no-restore -m:1
dotnet test tests/LedgerPilot.Domain.Tests -c Release --no-build
python3 -m unittest discover -s tests -p 'test_*.py' -v
cd web && npm ci && npm run build
```

The domain tests exercise real money, event replay, reconciliation and threshold behavior.
They are not proof of 44-rule or cloud end-to-end completion.

## Foundry configuration

Set `FOUNDRY_PROJECT_ENDPOINT` to the Foundry project URL and `FOUNDRY_MODEL`
to the deployed model name. Use Azure CLI identity locally. Never commit secrets.
The SDK code is compiled, but live inference requires the model deployment and RBAC.

## Azure rollout

Azure deployment is disabled for this local, unauthenticated foundation.
See [the implementation plan](docs/IMPLEMENTATION_PLAN.md) and
[Azure access requirements](docs/AZURE_ACCESS.md). No cloud resources have been provisioned.
The API rejects non-Development startup and non-loopback requests.

## Source-of-truth restrictions

- The rule registry contains 44 rules (34 deterministic, 7 hybrid, 3 AI-assisted); this source sample does **not** yet implement all 44.
- The accrual trigger is not consistent across provided documents. Current sample labels `Proposed amount - available WIP > 10000` as an assumption requiring business agreement.
- The mock API has no Entra auth and mock PBD events are held in process memory. Do not use real or sensitive data. The Cosmos demonstration writes are not durable replacement for idempotent transaction state.
- Actual financial posting cannot be validated without a real SAP sandbox tenant, permitted APIs and contracts.

## High-priority work for team handover

1. Expand the compiled, pinned foundation through all-rule acceptance tests.
2. Implement Entra ID JWT + project-level RBAC and anti-forgery/rate limits.
3. Implement complete deterministic and hybrid rules with golden cases, VAT/FX/rounding and fixed price schedule.
4. Make state/approval/idempotency entirely transactional, persistent and restart-safe.
5. Add Durable Functions isolated-worker orchestration; Service Bus mock SAP worker; transactional outbox and failure injection.
6. Add IaC (Bicep/Terraform) for all Azure resources, least-privilege identities, observability and secrets.
7. Integration-test Foundry SDK, private networking, and security before sharing externally.
