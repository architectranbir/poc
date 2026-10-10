variable "runtime_location" {
  type    = string
  default = "centralindia"
}
variable "enable_runtime_platform" {
  type    = bool
  default = false
}
variable "postgres_admin_password" {
  type      = string
  sensitive = true
  default   = ""
  validation {
    condition     = !var.enable_runtime_platform || can(regex("^[A-Za-z0-9!@#$%^&*()_+=.-]{20,}$", var.postgres_admin_password))
    error_message = "Runtime platform requires a PostgreSQL password of at least 20 letters, digits or supported symbols (!@#$%^&*()_+=.-)."
  }
}
resource "azurerm_container_registry" "runtime" {
  count               = var.enable_runtime_platform ? 1 : 0
  name                = "acrlp${var.name_suffix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}
resource "azurerm_virtual_network" "runtime" {
  count               = var.enable_runtime_platform ? 1 : 0
  name                = "vnet-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.runtime_location
  address_space       = ["10.42.0.0/16"]
  tags                = local.tags
}
resource "azurerm_subnet" "containers" {
  count                = var.enable_runtime_platform ? 1 : 0
  name                 = "containers"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.runtime[0].name
  address_prefixes     = ["10.42.0.0/23"]
}
resource "azurerm_subnet" "postgres" {
  count                = var.enable_runtime_platform ? 1 : 0
  name                 = "postgres"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.runtime[0].name
  address_prefixes     = ["10.42.2.0/24"]
  delegation {
    name = "postgres"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}
resource "azurerm_private_dns_zone" "postgres" {
  count               = var.enable_runtime_platform ? 1 : 0
  name                = "${var.name_suffix}.postgres.database.azure.com"
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.tags
}
resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  count                 = var.enable_runtime_platform ? 1 : 0
  name                  = "postgres-runtime"
  resource_group_name   = azurerm_resource_group.main.name
  private_dns_zone_name = azurerm_private_dns_zone.postgres[0].name
  virtual_network_id    = azurerm_virtual_network.runtime[0].id
}
resource "azurerm_postgresql_flexible_server" "runtime" {
  count                         = var.enable_runtime_platform ? 1 : 0
  name                          = "pg-${local.prefix}"
  resource_group_name           = azurerm_resource_group.main.name
  location                      = var.runtime_location
  version                       = "17"
  administrator_login           = "ledgerpilotadmin"
  administrator_password        = var.postgres_admin_password
  sku_name                      = "B_Standard_B1ms"
  storage_mb                    = 32768
  backup_retention_days         = 7
  public_network_access_enabled = false
  delegated_subnet_id           = azurerm_subnet.postgres[0].id
  private_dns_zone_id           = azurerm_private_dns_zone.postgres[0].id
  depends_on                    = [azurerm_private_dns_zone_virtual_network_link.postgres]
  tags                          = local.tags
}
resource "azurerm_postgresql_flexible_server_database" "runtime" {
  count     = var.enable_runtime_platform ? 1 : 0
  name      = "ledgerpilot"
  server_id = azurerm_postgresql_flexible_server.runtime[0].id
  charset   = "UTF8"
  collation = "en_US.utf8"
}
resource "azurerm_container_app_environment" "runtime" {
  count                      = var.enable_runtime_platform ? 1 : 0
  name                       = "cae-${local.prefix}"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = var.runtime_location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  infrastructure_subnet_id   = azurerm_subnet.containers[0].id
  tags                       = local.tags
}
output "container_registry_login_server" {
  value = try(azurerm_container_registry.runtime[0].login_server, null)
}
output "postgres_fqdn" {
  value = try(azurerm_postgresql_flexible_server.runtime[0].fqdn, null)
}
output "container_app_environment_id" {
  value = try(azurerm_container_app_environment.runtime[0].id, null)
}
