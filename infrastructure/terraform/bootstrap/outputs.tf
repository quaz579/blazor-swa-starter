output "resource_group_name" {
  value       = azurerm_resource_group.state.name
  description = "Resource group holding the state storage account. Use as backend.resource_group_name in ../versions.tf."
}

output "storage_account_name" {
  value       = azurerm_storage_account.state.name
  description = "Storage account holding Terraform state. Use as backend.storage_account_name in ../versions.tf."
}

output "container_name" {
  value       = azurerm_storage_container.tfstate.name
  description = "Blob container holding Terraform state. Use as backend.container_name in ../versions.tf."
}
