# Deploy

## Provisioning a fresh clone

```sh
gh repo create <owner>/<repo> --public --source=. --push
cd infrastructure/terraform
export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
export GITHUB_TOKEN=$(gh auth token)
terraform init
terraform apply
```

`az` and `gh` must already be logged in — a cold agent typically has both authenticated but
neither environment variable set. `terraform apply` needs `ARM_SUBSCRIPTION_ID` to resolve the
`azurerm` provider (the example `terraform.tfvars.example` leaves `subscription_id` unset for
exactly this reason) and `GITHUB_TOKEN` with `repo` scope so the `github` provider can write the
Actions secret in `github.tf`.

Copy `infrastructure/terraform/terraform.tfvars.example` to `terraform.tfvars` and fill in your
own values. This repo's own deployment used:

```hcl
project_name      = "swastarter"
github_owner      = "quaz579"
github_repository = "blazor-swa-starter"
location          = "eastus2"
```

`terraform` on a machine with OpenTofu installed may be a shell alias to `tofu` — either works;
the lock file records `registry.opentofu.org` sources.

**`terraform.tfstate` holds the Static Web Apps deployment token in plaintext** (it's the
resource's `api_key` attribute) — this is why `*.tfstate*` is gitignored from the first commit,
and why `terraform.tfstate` must never be committed or pasted anywhere.

### The ordering wrinkle

Terraform cannot create the GitHub repository it will live in before that repo exists, and it
cannot write the `AZURE_STATIC_WEB_APPS_API_TOKEN` secret before it runs. If you push to `main`
(the `--push` above) before `terraform apply` has completed, CI's first run is red at the
`deploy` step by construction — the secret doesn't exist yet. `ci.yml` has `workflow_dispatch`
enabled for exactly this: re-run the failed workflow from the Actions tab once `apply` finishes,
or just push again. Creating the repo *without* `--push`, running `terraform apply` to
completion, and pushing only afterward avoids the red run entirely.

## CI/CD

Every push to `main` and every pull request runs `.github/workflows/ci.yml`: `build-test`
(restore/build/`dotnet test`) → `e2e` (`scripts/start-e2e.sh`, the full Playwright suite,
`scripts/stop-e2e.sh`) → `deploy` (`Azure/static-web-apps-deploy@v1`, reading the
`AZURE_STATIC_WEB_APPS_API_TOKEN` secret Terraform wrote) → `smoke-preview` on pull requests
only, which waits for the preview URL to respond and then runs `npx playwright test --grep
@smoke` against it. Each job depends on the previous one (`needs:`), so unit and E2E failures
block a deploy.

### PR previews

A pull request gets its own Static Web Apps preview slot, deployed by the same `deploy` job.
**A preview environment inherits the production Static Web App's app settings — including the
production storage connection string.** It is not an isolated sandbox; only the read-only
`@smoke` spec (`tests/specs/smoke.spec.ts`) is safe to run against one, which is what
`smoke-preview` does. Closing the PR runs `.github/workflows/pr-cleanup.yml`, which tears the
preview slot down.

## Teardown

```sh
cd infrastructure/terraform
terraform destroy
```

Designed to be repeatable: `terraform apply` afterward re-provisions everything (a random
6-character suffix keeps the storage account and Log Analytics workspace names unique across a
destroy/apply cycle), and re-runs `github.tf` to rewrite the deploy-token secret.

## Why .NET 9, not 10

.NET 9 is Standard-Term-Support and reaches end of life 2026-11-10 — the same day as .NET 8. It
is used here not because it "extends support," but because `dotnet-isolated:9.0` is the ceiling
Azure Static Web Apps **managed functions** currently accept; there is no `dotnet-isolated:10.0`
runtime option. Bumping `net9.0` to `net10.0` will build locally and then fail to deploy.

## Verified this session

`terraform init`, `terraform validate`, `terraform fmt -check -recursive`, and a read-only
`terraform plan` (with `ARM_SUBSCRIPTION_ID`/`GITHUB_TOKEN` exported as above) all ran
successfully against this repo's live state. `terraform apply` and `terraform destroy` were
**not** run — this repo's Terraform state already describes real, deployed Azure resources, and
mutating it is outside what this doc-writing pass verifies. The `gh repo create` step above was
not re-run either, since the repository already exists.
