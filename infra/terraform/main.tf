terraform {
  required_version = "~> 1.13.0"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "= 4.75.0" }
  }
  backend "azurerm" {
    use_oidc         = true
    use_azuread_auth = true
  }
}
provider "azurerm" {
  features {}
  use_oidc                       = true
  storage_use_azuread            = true
  resource_providers_to_register = ["Microsoft.App", "Microsoft.DBforPostgreSQL"]
}
variable "location" {
  type    = string
  default = "southindia"
}
variable "name_suffix" {
  type = string
  validation {
    condition     = can(regex("^[a-z0-9]{6,10}$", var.name_suffix))
    error_message = "Use 6–10 lowercase letters or digits for globally unique resource names."
  }
}
locals {
  prefix = "ledgerpilot-${var.name_suffix}"
  tags   = { project = "LedgerPilot", environment = "poc", managed_by = "Terraform" }
}
resource "azurerm_resource_group" "main" {
  name     = "rg-${local.prefix}"
  location = var.location
  tags     = local.tags
}
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = 1
  tags                = local.tags
}
resource "azurerm_storage_account" "workflow" {
  name                            = "stlp${var.name_suffix}"
  resource_group_name             = azurerm_resource_group.main.name
  location                        = var.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  tags                            = local.tags
}
resource "azurerm_servicebus_namespace" "main" {
  name                = "sb-${local.prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Standard"
  minimum_tls_version = "1.2"
  local_auth_enabled  = false
  tags                = local.tags
}
resource "azurerm_servicebus_queue" "commands" {
  name                                    = "sap-commands"
  namespace_id                            = azurerm_servicebus_namespace.main.id
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = "PT10M"
  max_delivery_count                      = 5
  dead_lettering_on_message_expiration    = true
}
output "resource_group_name" { value = azurerm_resource_group.main.name }
output "storage_account_name" { value = azurerm_storage_account.workflow.name }
output "servicebus_namespace_id" { value = azurerm_servicebus_namespace.main.id }
