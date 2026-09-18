# Optional remote-state backend for the root module.
#
# The root module (../) uses local state by default so a fresh clone can run
# `tofu init && tofu apply` with nothing else provisioned first. That's fine for a
# single developer or a throwaway environment, but local state has no locking and
# isn't shared — a second contributor or a CI runner applying from a different
# machine will diverge from it. This module creates a small storage account +
# blob container to hold state remotely instead, with locking, via Terraform's
# built-in `azurerm` backend.
#
# Usage:
#   1. cd infrastructure/terraform/bootstrap
#   2. tofu init && tofu apply
#   3. Copy this module's outputs into the commented-out `backend "azurerm"` block
#      in ../versions.tf, uncomment it, then `tofu init -migrate-state` from ../.
#
# This module's own state is intentionally local (see versions.tf) — it creates
# the backend, so it cannot be the first thing to use it.

resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

resource "azurerm_resource_group" "state" {
  name     = "rg-${var.project_name}-tfstate"
  location = var.location
  tags     = var.tags
}

resource "azurerm_storage_account" "state" {
  name                     = "st${replace(var.project_name, "-", "")}tfstate${random_string.suffix.result}"
  resource_group_name      = azurerm_resource_group.state.name
  location                 = azurerm_resource_group.state.location
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  https_traffic_only_enabled = true

  # State holds resource secrets in plaintext (e.g. the SWA deployment token);
  # versioning gives a recovery path if a bad apply overwrites state before its
  # damage is noticed, and blob soft delete does the same for accidental deletes.
  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }
  }

  tags = var.tags
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
}
