variable "demo_public_access" {
  type    = bool
  default = false
}
variable "enable_workloads" {
  type    = bool
  default = false
}
variable "image_tag" {
  type    = string
  default = ""
}
variable "demo_allowed_ipv4_cidrs" {
  type    = list(string)
  default = []
  validation {
    condition     = alltrue([for cidr in var.demo_allowed_ipv4_cidrs : can(cidrnetmask(cidr)) && try(tonumber(split("/", cidr)[1]) >= 24, false)])
    error_message = "Use explicit IPv4 ranges /24 or narrower for demo access."
  }
}
check "workload_inputs" {
  assert {
    condition     = !var.enable_workloads || (var.enable_runtime_platform && can(regex("^[a-f0-9]{40}$", var.image_tag)) && (var.demo_public_access || length(var.demo_allowed_ipv4_cidrs) > 0))
    error_message = "Workloads require the runtime platform, a published commit image tag and explicit public synthetic access or restricted demo IP ranges."
  }
}
locals {
  apps          = var.enable_workloads ? toset(["demo", "sap", "dispatcher", "workflows"]) : toset([])
  pg_connection = var.enable_runtime_platform ? "Host=${azurerm_postgresql_flexible_server.runtime[0].fqdn};Username=ledgerpilotadmin;Password=${var.postgres_admin_password};Database=ledgerpilot;SSL Mode=VerifyFull" : ""
  runtime_env = {
    SYNTHETIC_HOSTED_DEMO = "true"
    DOTNET_ENVIRONMENT    = "Production"
    AZURE_CLIENT_ID       = try(azurerm_user_assigned_identity.workload[0].client_id, "")
    SERVICEBUS_NAMESPACE  = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
    SERVICEBUS_QUEUE      = azurerm_servicebus_queue.commands.name
  }
  storage_env = {
    FUNCTIONS_WORKER_RUNTIME         = "dotnet-isolated"
    AZURE_FUNCTIONS_ENVIRONMENT      = "Production"
    WEBSITE_HOSTNAME                 = "localhost"
    AzureWebJobsStorage__accountName = azurerm_storage_account.workflow.name
    AzureWebJobsStorage__credential  = "managedidentity"
    AzureWebJobsStorage__clientId    = try(azurerm_user_assigned_identity.workload[0].client_id, "")
  }
  containers = {
    demo = [{ name = "demo", image = "demo", env = { ASPNETCORE_ENVIRONMENT = "Production", AUTH_MODE = "HostedDemo", DEMO_NETWORK_RESTRICTED = tostring(!var.demo_public_access), SYNTHETIC_PUBLIC_DEMO = tostring(var.demo_public_access) }, cpu = 0.5, memory = "1Gi" }]
    sap = [
      { name = "simulator", image = "sapsimulator", env = { ASPNETCORE_ENVIRONMENT = "Production", ASPNETCORE_URLS = "http://127.0.0.1:8090" }, cpu = 0.25, memory = "0.5Gi" },
      { name = "worker", image = "sapworker", env = { SAP_TRANSPORT = "ServiceBus", SAP_SIMULATOR_URL = "http://127.0.0.1:8090", SAP_COMMAND_PATH = "/tmp/commands" }, cpu = 0.25, memory = "0.5Gi" }
    ]
    dispatcher = [{ name = "dispatcher", image = "outboxdispatcher", env = {}, cpu = 0.25, memory = "0.5Gi" }]
    workflows  = [{ name = "workflows", image = "workflows", env = local.storage_env, cpu = 0.5, memory = "1Gi" }]
  }
}
resource "azurerm_user_assigned_identity" "workload" {
  count               = var.enable_runtime_platform ? 1 : 0
  name                = "id-${local.prefix}-runtime"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.tags
}
resource "azurerm_role_assignment" "pull" {
  count                = var.enable_runtime_platform ? 1 : 0
  scope                = azurerm_container_registry.runtime[0].id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.workload[0].principal_id
}
resource "azurerm_role_assignment" "bus" {
  for_each             = var.enable_runtime_platform ? toset(["Azure Service Bus Data Sender", "Azure Service Bus Data Receiver"]) : toset([])
  scope                = azurerm_servicebus_queue.commands.id
  role_definition_name = each.key
  principal_id         = azurerm_user_assigned_identity.workload[0].principal_id
}
resource "azurerm_role_assignment" "workflow_storage" {
  for_each             = var.enable_runtime_platform ? toset(["Storage Blob Data Owner", "Storage Queue Data Contributor", "Storage Table Data Contributor"]) : toset([])
  scope                = azurerm_storage_account.workflow.id
  role_definition_name = each.key
  principal_id         = azurerm_user_assigned_identity.workload[0].principal_id
}
resource "azurerm_container_app" "runtime" {
  for_each                     = local.apps
  name                         = "${local.prefix}-${each.key}"
  container_app_environment_id = azurerm_container_app_environment.runtime[0].id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = local.tags
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.workload[0].id]
  }
  registry {
    server   = azurerm_container_registry.runtime[0].login_server
    identity = azurerm_user_assigned_identity.workload[0].id
  }
  secret {
    name  = "postgres"
    value = local.pg_connection
  }
  dynamic "ingress" {
    for_each = each.key == "demo" ? [1] : []
    content {
      external_enabled           = true
      target_port                = 8080
      allow_insecure_connections = false
      transport                  = "http"
      dynamic "ip_security_restriction" {
        for_each = var.demo_public_access ? toset([]) : toset(var.demo_allowed_ipv4_cidrs)
        content {
          name             = "allow-${replace(replace(ip_security_restriction.value, ".", "-"), "/", "-")}"
          action           = "Allow"
          ip_address_range = ip_security_restriction.value
        }
      }
      traffic_weight {
        latest_revision = true
        percentage      = 100
      }
    }
  }
  template {
    min_replicas = 1
    max_replicas = 1
    dynamic "container" {
      for_each = local.containers[each.key]
      content {
        name   = container.value.name
        image  = "${azurerm_container_registry.runtime[0].login_server}/ledgerpilot-${container.value.image}:${var.image_tag}"
        cpu    = container.value.cpu
        memory = container.value.memory
        env {
          name        = "COMMAND_STORE_CONNECTION"
          secret_name = "postgres"
        }
        dynamic "env" {
          for_each = container.value.name == "simulator" ? [1] : []
          content {
            name        = "SAP_STATE_CONNECTION"
            secret_name = "postgres"
          }
        }
        dynamic "env" {
          for_each = merge(local.runtime_env, container.value.env)
          content {
            name  = env.key
            value = env.value
          }
        }
        dynamic "readiness_probe" {
          for_each = each.key == "demo" ? [1] : []
          content {
            transport = "HTTP"
            port      = 8080
            path      = "/health"
          }
        }
      }
    }
  }
  lifecycle {
    precondition {
      condition     = var.enable_runtime_platform && can(regex("^[a-f0-9]{40}$", var.image_tag)) && (var.demo_public_access || length(var.demo_allowed_ipv4_cidrs) > 0)
      error_message = "Workload deployment requires published images and explicit public synthetic access or restricted demo ingress."
    }
  }
  depends_on = [azurerm_role_assignment.pull, azurerm_role_assignment.bus, azurerm_role_assignment.workflow_storage, azurerm_postgresql_flexible_server_database.runtime]
}
output "demo_url" {
  value = try("https://${azurerm_container_app.runtime["demo"].ingress[0].fqdn}", null)
}
