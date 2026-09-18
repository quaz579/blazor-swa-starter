resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  resource_prefix = "${var.project_name}-${var.environment}"

  # Storage account and Log Analytics workspace names must be globally/region unique
  # and survive a destroy+re-apply cycle, hence the random suffix. Storage account
  # names additionally allow only lowercase letters/digits, hence stripping hyphens.
  name_prefix_alnum    = lower(replace(local.resource_prefix, "-", ""))
  storage_account_name = "st${local.name_prefix_alnum}${random_string.suffix.result}"

  log_analytics_workspace_name = "law-${local.resource_prefix}-${random_string.suffix.result}"
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.resource_prefix}"
  location = var.location
  tags     = var.tags
}

resource "azurerm_storage_account" "main" {
  name                     = local.storage_account_name
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  https_traffic_only_enabled = true

  blob_properties {
    cors_rule {
      allowed_headers    = ["*"]
      allowed_methods    = ["GET", "POST", "DELETE", "OPTIONS", "PUT", "HEAD"]
      allowed_origins    = var.allowed_origins
      exposed_headers    = ["*"]
      max_age_in_seconds = 3600
    }
  }

  tags = var.tags
}

resource "azurerm_storage_container" "app_data" {
  name                  = "app-data"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = local.log_analytics_workspace_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = var.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.resource_prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.main.id
  tags                = var.tags
}

resource "azurerm_static_web_app" "main" {
  name                = "swa-${local.resource_prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  sku_tier            = "Free"
  sku_size            = "Free"

  # These two settings are what make the deployed managed API's blob round-trip
  # work at all — without them the live site's /api/items fails even though local
  # dev against Azurite (which the API resolves separately) succeeds.
  app_settings = {
    AZURE_STORAGE_CONNECTION_STRING       = azurerm_storage_account.main.primary_connection_string
    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.main.connection_string
  }

  tags = var.tags

  lifecycle {
    # The deploy action stamps the source repo and branch onto the resource on
    # every upload. Terraform never sets them, so without this a later apply
    # would keep trying to clear them back out.
    ignore_changes = [repository_url, repository_branch]
  }
}
