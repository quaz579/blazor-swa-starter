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
  }

  # This bootstrap module's own state stays local deliberately: it exists to create
  # the backend that the root module's state moves into, so it can't depend on that
  # backend itself. Run it once, note the outputs, then apply them to the
  # `backend "azurerm"` block commented out in ../versions.tf.
}

provider "azurerm" {
  features {}

  subscription_id = var.subscription_id
}
