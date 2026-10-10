# GitHub Terraform deployment foundation

Terraform is the selected infrastructure tool, replacing the earlier Bicep proposal. This first module defines the dedicated resource group, Log Analytics, workflow storage and Service Bus command queue in South India. It does not yet define Container Apps, PostgreSQL, Durable Functions, identities/RBAC, Key Vault or Foundry/model deployment. No Azure resources have been deployed.

Terraform 1.13.5 and AzureRM 4.75.0 are pinned. The provider ZIP downloaded from HashiCorp matches the published SHA256; the lockfile currently supports linux_amd64 (GitHub Ubuntu runners). Run terraform providers lock for additional platforms before using other workstations. Formatting passes. Provider schema validation could not run locally because the provider process failed its startup handshake; this is not a successful validation or plan. GitHub CI must validate against the provider before deployment.

## Bootstrap and OIDC
Create the Azure Storage state backend separately with a private blob container, Entra authentication and appropriate retention. The deployment principal needs Storage Blob Data Contributor on that container and provisioning permissions scoped to the intended PoC resources. This bootstrap is necessary before the main module can store its state; it is not yet automated here.

Create a GitHub-federated Azure deployment identity. Add federated subjects for repo:architectranbir/poc:environment:azure-poc-plan and repo:architectranbir/poc:environment:azure-poc-apply. Configure both GitHub environments, main-branch restrictions and an apply reviewer if available for this repository/account plan. Environment approval enforcement is a GitHub configuration dependency; YAML alone does not enforce reviewer approval.

Repository variables required: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, AZURE_NAME_SUFFIX, TF_STATE_RG, TF_STATE_ACCOUNT, TF_STATE_CONTAINER. IDs are configuration; no client secret or storage key is used. Actual subscription identifiers remain out of committed files.

The manually dispatched Terraform workflow accepts plan or apply, only on main. Apply consumes that run's saved plan in the separate apply environment. Concurrency prevents overlapping runs. State uses Azure Blob locking and OIDC/Entra authentication. Plan artifact retention is one day; protect access to plans/state because they may contain sensitive values as infrastructure expands.

## Before any apply
Verify the actual subscription offer/credit, provider registrations, South India service/SKU eligibility, regional pricing, global name uniqueness and role permissions. Add the remaining runtime modules and managed identity assignments before application deployment. Shared API hosting remains disabled until transactional persistence and complete authorization/workflow gates are ready.

Official references:
- https://developer.hashicorp.com/terraform/language/backend/azurerm
- https://registry.terraform.io/providers/hashicorp/azurerm/4.75.0/docs/guides/service_principal_oidc
