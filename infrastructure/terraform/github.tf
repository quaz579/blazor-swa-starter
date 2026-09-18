provider "github" {
  owner = var.github_owner
  # Reads GITHUB_TOKEN from the environment; requires "repo" scope to write an
  # Actions secret to the target repository.
}

resource "github_actions_secret" "swa_deploy_token" {
  repository  = var.github_repository
  secret_name = "AZURE_STATIC_WEB_APPS_API_TOKEN"
  value       = azurerm_static_web_app.main.api_key
}
