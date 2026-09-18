variable "subscription_id" {
  description = "Azure subscription ID. Leave null to fall back to the ARM_SUBSCRIPTION_ID environment variable (azurerm 4.x requires the provider to resolve one or the other)."
  type        = string
  default     = null
}

variable "project_name" {
  description = "Short project name used to build resource names. Lowercase letters, digits, and hyphens only, max 10 characters — the generated storage account name (\"st\" + project_name + environment + a 6-char random suffix, hyphens stripped) must stay within Azure's 24-character storage account name limit, and environment is capped at 6 characters for the same reason."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$", var.project_name)) && length(var.project_name) <= 10
    error_message = "project_name must be 1-10 characters, lowercase letters/digits/hyphens only, starting and ending with a letter or digit — required so the generated storage account name fits Azure's 24-character limit."
  }
}

variable "environment" {
  description = "Deployment environment, used in resource names (e.g. dev, test, prod). Lowercase letters, digits, and hyphens only, max 6 characters — combined with a 10-character project_name, a 2-character prefix, and a 6-character random suffix, this is what keeps the generated storage account name within Azure's 24-character limit."
  type        = string
  default     = "dev"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$", var.environment)) && length(var.environment) <= 6
    error_message = "environment must be 1-6 characters, lowercase letters/digits/hyphens only, starting and ending with a letter or digit."
  }
}

variable "location" {
  description = "Azure region. Must be one of the regions Azure Static Web Apps supports: westus2, centralus, eastus2, westeurope, eastasia."
  type        = string
  default     = "eastus2"

  validation {
    condition     = contains(["westus2", "centralus", "eastus2", "westeurope", "eastasia"], var.location)
    error_message = "location must be one of: westus2, centralus, eastus2, westeurope, eastasia (the regions Azure Static Web Apps supports)."
  }
}

variable "github_owner" {
  description = "GitHub org or user that owns the repository the deployment token secret is written to."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository name (without the owner prefix) that the deployment token secret is written to."
  type        = string
}

variable "allowed_origins" {
  description = "Origins allowed to call the storage account's blob endpoint via CORS. Defaults to the local SWA CLI dev server. Add the deployed Static Web App's URL (https://<static_web_app_url output>) after the first apply — referencing it directly here would create a dependency cycle, since the Static Web App's app_settings already depend on the storage account."
  type        = list(string)
  default     = ["http://localhost:4280"]
}

variable "tags" {
  description = "Tags applied to every taggable resource."
  type        = map(string)
  default = {
    Project   = "blazor-swa-starter"
    ManagedBy = "Terraform"
  }
}
