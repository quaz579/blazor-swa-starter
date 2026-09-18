variable "subscription_id" {
  description = "Azure subscription ID. Leave null to fall back to the ARM_SUBSCRIPTION_ID environment variable."
  type        = string
  default     = null
}

variable "project_name" {
  description = "Short project name used to build the state storage account's resource names. Lowercase letters, digits, and hyphens only, max 8 characters — mirrors the same limit as the root module's project_name."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$", var.project_name)) && length(var.project_name) <= 8
    error_message = "project_name must be 1-8 characters, lowercase letters/digits/hyphens only, starting and ending with a letter or digit."
  }
}

variable "location" {
  description = "Azure region for the state storage account. Any standard Azure region — this resource, unlike the Static Web App, isn't restricted to a small region list."
  type        = string
  default     = "eastus2"
}

variable "tags" {
  description = "Tags applied to the state storage resources."
  type        = map(string)
  default = {
    Project   = "blazor-swa-starter"
    ManagedBy = "Terraform"
    Purpose   = "tfstate-backend"
  }
}
