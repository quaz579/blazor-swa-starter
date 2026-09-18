output "static_web_app_url" {
  value       = "https://${azurerm_static_web_app.main.default_host_name}"
  description = "Public URL of the deployed Static Web App."
}

output "static_web_app_name" {
  value       = azurerm_static_web_app.main.name
  description = "Name of the Static Web App resource."
}

output "resource_group_name" {
  value       = azurerm_resource_group.main.name
  description = "Name of the resource group holding every resource this module creates."
}

output "storage_account_name" {
  value       = azurerm_storage_account.main.name
  description = "Name of the storage account backing blob storage."
}

output "application_insights_connection_string" {
  value       = azurerm_application_insights.main.connection_string
  description = "Application Insights connection string used by the API and Web app."
  sensitive   = true
}

output "static_web_app_api_key" {
  value       = azurerm_static_web_app.main.api_key
  description = "Static Web App deployment token. Also written to the GitHub repository as the AZURE_STATIC_WEB_APPS_API_TOKEN secret by github.tf."
  sensitive   = true
}
