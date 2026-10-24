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
```

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

The public starter uses hosted CI by default. Repository variable `RUNNER_PROFILE=org-app`
enables the nuc profile only for private `bengrossmandotus` repositories. PRs require a
same-repo head and OWNER, MEMBER or COLLABORATOR author association; fork/untrusted PRs
stay hosted and never run the status selector. CI and preview cleanup retain their original
job IDs and test/deploy prerequisites.

Provision org apps with `RUNNER_FALLBACK_ENABLED=true`: prefer the nuc and select hosted
when it is busy or offline. The public starter and software-factory CI defaults are unchanged.

With `RUNNER_MODE=hosted`, all jobs go directly to hosted without selection. Otherwise,
`RUNNER_FALLBACK_ENABLED=true` enables checkout-free `ubuntu-slim` selection at each job
boundary using the repository secret `RUNNER_STATUS_TOKEN`. It checks paginated org inventory
for idle online `factory-apps-1` with `self-hosted`, `Linux`, `X64`, `factory-apps` labels.
Missing credentials, invalid inventory or bounded API/transport failures choose hosted.
Auth E2E selection waits for E2E to finish so the fanout cannot share one idle snapshot.
A denied/failed enabled selector blocks dependent jobs rather than bypassing CI.

Missing/false `RUNNER_FALLBACK_ENABLED` skips every hosted selector and sends trusted jobs,
including deploy and smoke, directly to the nuc. Clear forced-hosted mode before relying on
this zero-hosted-minutes configuration. An unavailable nuc then requires infrastructure repair;
no executable fallback exists without hosted entitlement. A selector snapshot is not a runner
reservation; queue failover still requires the separate control-plane watchdog.

Before first push, provisioning must verify the repo is private, install repository-level
selector variables and the status secret, and verify selected runner-group access for that
repo ID. Labels and runner inventory do not establish authorization. Never rely on org secret
inheritance. Status credentials need only org runner-read and belong only to trusted preflight;
factory/personal credentials and runner enrollment credentials never belong in app jobs.
Keep the profile disabled until the isolated unprivileged app runner, single-slot capacity,
rootless Docker Azure deployment and live fallback proofs pass. The profile does not enable
or alter the factory's separate Azure per-product runner feature.

Org workflow defaults are read-only under enterprise policy and cannot be changed. Every
app workflow job declares explicit least-privilege token permissions: selectors use `{}`,
checkout jobs use `contents: read`, and SWA upload jobs add `pull-requests: write` for preview
comments. No job uses OIDC. `cd tests && npm run check:workflows` rejects missing permissions,
including in Copilot setup and preview cleanup; `npm test` runs this check first. A real PR
preview deploy under the read-only default remains required in canary `sf-7tl.5`.
