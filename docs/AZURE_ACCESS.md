# Azure deployment inputs

Identify the target subscription ID, tenant ID and region. Confirm the Free Trial
offer/expiry, remaining credit, model quota and deployment permissions.
Do not commit passwords, tokens, client secrets, connection strings or private keys.

GitHub Actions deployment will use Entra workload identity federation (OIDC).
The repository will need non-secret tenant/subscription/client identifiers and a
deployment identity scoped to the dedicated PoC resource group.

This baseline cannot start outside Development, and rejects non-loopback requests.
Remove these restrictions only when Entra authentication, project authorization and
durable submission gates are implemented and tested. The old public deployment script is disabled.

Planned infrastructure: Container Apps API/worker/simulator, isolated-worker Durable
Functions, Service Bus, transactional database, Storage, Foundry and Application Insights.
No Azure resources have been provisioned.
