terraform {
  required_version = "~> 1.13.0"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "= 4.75.0" }
  }
}
variable "subscription_id" { type = string }
variable "location" {
  type    = string
  default = "southindia"
}
variable "name_suffix" {
  type = string
  validation {
    condition     = can(regex("^[a-z0-9]{6,10}$", var.name_suffix))
    error_message = "Use 6–10 lowercase letters or digits."
  }
}
provider "azurerm" {
  features {}
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}
data "azurerm_client_config" "current" {}
resource "azurerm_resource_group" "bootstrap" {
  name     = "rg-ledgerpilot-${var.name_suffix}-bootstrap"
  location = var.location
}
resource "azurerm_user_assigned_identity" "github" {
  name                = "id-ledgerpilot-${var.name_suffix}-github"
  resource_group_name = azurerm_resource_group.bootstrap.name
  location            = var.location
}
resource "azurerm_federated_identity_credential" "github" {
  for_each            = toset(["azure-poc-plan", "azure-poc-apply"])
  name                = "${each.key}-v2"
  resource_group_name = azurerm_resource_group.bootstrap.name
  parent_id           = azurerm_user_assigned_identity.github.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:architectranbir@286104637/poc@1412939246:environment:${each.key}"
}
resource "azurerm_role_assignment" "deployment" {
  scope                = "/subscriptions/${var.subscription_id}"
  role_definition_name = "Owner"
  principal_id         = azurerm_user_assigned_identity.github.principal_id
}
resource "azurerm_storage_account" "state" {
  name                            = "stlptf${var.name_suffix}"
  resource_group_name             = azurerm_resource_group.bootstrap.name
  location                        = var.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  allow_nested_items_to_be_public = false
}
resource "azurerm_role_assignment" "state_writer" {
  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.github.principal_id
}
resource "azurerm_role_assignment" "bootstrap_writer" {
  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
resource "azurerm_storage_container" "state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
  depends_on            = [azurerm_role_assignment.bootstrap_writer]
}
output "github_variables" {
  value = {
    AZURE_CLIENT_ID       = azurerm_user_assigned_identity.github.client_id
    AZURE_TENANT_ID       = data.azurerm_client_config.current.tenant_id
    AZURE_SUBSCRIPTION_ID = var.subscription_id
    AZURE_NAME_SUFFIX     = var.name_suffix
    AZURE_REGION          = var.location
    TF_STATE_RG           = azurerm_resource_group.bootstrap.name
    TF_STATE_ACCOUNT      = azurerm_storage_account.state.name
    TF_STATE_CONTAINER    = azurerm_storage_container.state.name
    AZURE_ACR_NAME        = "acrlp${var.name_suffix}"
    AZURE_ACR_SERVER      = "acrlp${var.name_suffix}.azurecr.io"
  }
}
