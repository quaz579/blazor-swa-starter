terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    github = {
      source  = "integrations/github"
      version = "~> 6.0"
    }
  }

  # Local state is the default so a fresh clone can `tofu init && tofu apply`
  # immediately. To move state into Azure Storage instead (recommended once more
  # than one person/CI runs apply), first run `infrastructure/terraform/bootstrap`
  # — it provisions the storage account this backend points at — then uncomment:
  #
  # backend "azurerm" {
  #   resource_group_name  = "<bootstrap-resource-group>"
  #   storage_account_name = "<bootstrap-storage-account>"
  #   container_name       = "tfstate"
  #   key                  = "blazor-swa-starter.tfstate"
  # }
}

provider "azurerm" {
  features {
    # Log Analytics soft-deletes a workspace for 14 days by default, which breaks
    # `destroy` followed by `apply` under the same name.
    log_analytics_workspace {
      permanently_delete_on_destroy = true
    }
  }

  subscription_id = var.subscription_id
}
