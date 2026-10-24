# Deploy

## Provisioning a fresh clone

```sh
gh repo create <owner>/<repo> --public --source=.
cd infrastructure/terraform
export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
export GITHUB_TOKEN=$(gh auth token)
terraform init
terraform apply
git push -u origin main
```

`az` and `gh` must already be logged in — a cold agent typically has both authenticated but
neither environment variable set. `terraform apply` needs `ARM_SUBSCRIPTION_ID` to resolve the
`azurerm` provider — `variables.tf` defaults `subscription_id` to `null` and falls back to that
environment variable; this repo's own `terraform.tfvars` omits the line entirely and relies on
the export — and `GITHUB_TOKEN` with `repo` scope so the `github` provider can write the Actions
secret in `github.tf`.

Copy `infrastructure/terraform/terraform.tfvars.example` to `terraform.tfvars`, delete or fill in
the placeholder `subscription_id` line, and set your own values. This repo's own deployment used:

```hcl
project_name      = "swastarter"
github_owner      = "quaz579"
github_repository = "blazor-swa-starter"
location          = "eastus2"
swa_location      = "centralus"
```

`location` keeps the resource group, storage, and telemetry in East US 2; `swa_location`
independently selects Central US for the SWA and its managed Functions API. Changing the
SWA region replaces the SWA, changes its randomly generated hostname and deployment token,
and removes its deployments and preview environments. Storage stays in East US 2 and keeps
its data; the API continues using the same storage connection string across regions.

For the existing starter, Ben applies from the authoritative state directory after reviewing
`tofu plan -var='swa_location=centralus'` (keep `location=eastus2`):

```sh
ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv) \
GITHUB_TOKEN=$(gh auth token --user quaz579) tofu apply -var='swa_location=centralus'
```

The regional change is **not applied by this PR**. After Ben applies, verify that `github.tf`
updated `AZURE_STATIC_WEB_APPS_API_TOKEN` to the replacement SWA's token, then retry the PR 2
deploy. Read `tofu output -raw static_web_app_url` for the new URL, update external consumers
and any explicit `allowed_origins`, redeploy production, and recreate needed PR previews.
Restore any settings, custom domains, linked backends, or role invitations added outside IaC
before relying on them; existing auth sessions and preview URLs cannot carry over.

The Terraform commands in this doc were exercised through OpenTofu (`.terraform.lock.hcl`
records `registry.opentofu.org` provider sources); `versions.tf` requires Terraform/OpenTofu
`>= 1.5.0`.

**`terraform.tfstate` holds the Static Web Apps deployment token in plaintext** (it's the
resource's `api_key` attribute) — this is why `*.tfstate*` is gitignored from the first commit,
and why `terraform.tfstate` must never be committed or pasted anywhere.

### The ordering wrinkle

Terraform cannot create the GitHub repository it will live in before that repo exists, and it
cannot write the `AZURE_STATIC_WEB_APPS_API_TOKEN` secret before it runs. Push to `main` (for
example via `gh repo create ... --push`, or any push before `terraform apply` has completed) and
CI's first run is red at the `deploy` step by construction — the secret doesn't exist yet.
`ci.yml` has `workflow_dispatch` enabled for exactly this: re-run the failed workflow from the
Actions tab once `apply` finishes, or just push again. The sequence above — create the repo
without pushing, run `terraform apply` to completion, push only afterward — avoids the red run
entirely.

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

## Write auth

The deployed demo is public, and `App.Api`'s Functions all carry `AuthorizationLevel.Anonymous`
by design (no function key) — the API would otherwise be world-writable at the HTTP layer. So
writes are gated one layer up, in `src/App.Web/wwwroot/staticwebapp.config.json`, via two route
rules:

```json
{
  "route": "/api/items",
  "methods": ["POST"],
  "allowedRoles": ["authenticated"]
},
{
  "route": "/api/items/*",
  "methods": ["DELETE"],
  "allowedRoles": ["authenticated"]
}
```

These rules belong to the Items sample (removed from generated products; use the same shape for your own write endpoints). `methods` scopes each rule to one verb, so `GET /api/items` and `GET /api/items/{id}` fall
through unmatched and stay anonymous. Static Web Apps enforces `allowedRoles` itself, in front
of the Function, using its built-in auth providers — Microsoft Entra ID and GitHub are
pre-configured on Free tier (a custom OIDC/OAuth provider needs Standard tier). A visitor signs
in at `/.auth/login/aad` or `/.auth/login/github` on the deployed URL (`/.auth/me` shows the
resulting principal); without a `role: authenticated` session, they get a 401 before the
request reaches `App.Api`. No Functions code, `AuthorizationLevel`, or `App.Core` change is
involved, and no endpoint is removed.

To open the API back up for your own fork, delete both route rules above from
`staticwebapp.config.json`. That's the entire change; nothing else references them.

Locally, the SWA CLI emulator enforces `allowedRoles` the same way, so the Playwright specs
that exercise `POST`/`DELETE` authenticate first — see `TESTING.md`.

## Teardown

```sh
cd infrastructure/terraform
terraform destroy
```

Designed to be repeatable: `terraform apply` afterward re-provisions everything (a random
6-character suffix keeps the storage account and Log Analytics workspace names unique across a
destroy/apply cycle), and re-runs `github.tf` to rewrite the deploy-token secret.

`apply`, `destroy`, and `gh repo create` mutate real resources, so they aren't something to
re-run casually against a repo that's already live — the read-only checks (`terraform init`,
`terraform validate`, `terraform fmt -check -recursive`, `terraform plan`) are what
`.claude/agents/swa-infra.md` runs by default, and what to reach for to confirm the
configuration is still sound without changing anything.

## Why .NET 9, not 10

.NET 9 is Standard-Term-Support and reaches end of life 2026-11-10 — the same day as .NET 8. It
is used here not because it "extends support," but because `dotnet-isolated:9.0` is the ceiling
Azure Static Web Apps **managed functions** currently accept — there is no `dotnet-isolated:10.0`
runtime option, so `net9.0` cannot be bumped to `net10.0` without breaking the managed-functions
deploy.

## Optional private org-app runner profile

Set `RUNNER_PROFILE=org-app` for private `bengrossmandotus` apps. Only same-repo PRs
from OWNER, MEMBER or COLLABORATOR authors qualify; other PRs stay hosted and cannot
access the status secret. Org apps default to `RUNNER_FALLBACK_ENABLED=true`: a
checkout-free hosted preflight uses `RUNNER_STATUS_TOKEN` (org runner-read) to prefer
online, idle `nuc-org-1` with label `factory-apps`, falling back to hosted on busy,
offline or API failure. `RUNNER_MODE=hosted` forces hosted without a preflight;
missing/false fallback sends eligible jobs directly to the nuc without a preflight.
The Default org runner group permits all repositories; no enrollment hook is needed.
Org workflow defaults are read-only. Every job declares `permissions:` explicitly;
`cd tests && npm run check:workflows` checks this explicitly.
The e2e and e2e-poc-auth jobs run in parallel after build-test for every profile;
the single nuc listener queues jobs when both selectors choose it.
