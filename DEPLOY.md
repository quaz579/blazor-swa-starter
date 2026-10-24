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

## Infrastructure CI

`.github/workflows/infra.yml` (`Infra`) plans same-repo PRs targeting `main` and applies pushes
to `main`, path-filtered to `infrastructure/terraform/**` and the workflow. Every job requires
`github.repository == 'quaz579/blazor-swa-starter'` and `STARTER_INFRA_CI=true`, keeping template
copies inert. Missing required variables fail explicitly after gating. `STARTER_INFRA_STATE_RG` defaults to
`rg-swastarter-dev`; both jobs reject any other value. Fork/Dependabot PRs
are skipped; the trigger is `pull_request`, never `pull_request_target` or `workflow_run`.
Each job grants `contents: read` and `id-token: write`; only plan grants `pull-requests: write`.
Actions are SHA-pinned, jobs have timeouts, plans cancel per PR, and applies serialize without
cancellation. The first shell step after checkout asserts HEAD equals the push SHA.

Two Azure OIDC identities replace stored credentials. Apply trusts exactly
`repo:quaz579/blazor-swa-starter:ref:refs/heads/main`, so credentials need no GitHub Environment.
Provider registration is disabled; plan uses `-lock=false`, apply uses backend blob lease locking.
Same-repo writers can execute PR code with the key-reading plan identity: trust collaborators
and protect main. Fork PRs receive no OIDC token or secrets from this workflow.

Deletes/replacements **fail the PR check and refuse apply**, reporting **destructive: needs
manual apply** with addresses/actions in the summary and upserted `<!-- starter-infra-plan -->`
PR comment. Review and run `tofu apply` with your own login; failures use summaries/checks,
not issues. Hard refusal needs no extra setup; a solo-owner `infra-destructive` Environment
is self-approvable, while subject trust already pins credentials to main. Requiring `Infra plan`
therefore means legitimate destructive PRs need an owner bypass to merge.

Main plans fresh against current state: PR plans are advisory. Reports include SHA-256 of
sorted `address:actions` lines for comparison (not an attribute-value attestation). The guard
and applying that job's exact saved `tfplan` enforce safety. Only plan takes
`-exclude=github_actions_secret.swa_deploy_token`; saved-plan apply needs no exclusion or GitHub
provider token. **SWA replacement refreshes `AZURE_STATIC_WEB_APPS_API_TOKEN` only via manual
apply without exclusion.** Public logs capture plan output to a runner file, showing only the
last 40 diagnostic lines on failure; comments/summaries expose only addresses/actions. No plan
artifacts are uploaded. `if: always()` deletes plans, JSON, logs and `.terraform`; diagnostic
tails can still contain sensitive error details.

Run these blocks in order in one Bash terminal with az and gh already logged in as the owner,
from the authoritative checkout. These owner commands have not been executed by the implementation
agent: Azure/GitHub writes and state migration require the owner's login and real state.
`~/.starter-infra-ci.env` contains names and IDs only. Complete PR #18's manual apply before step 1;
the app RG must already exist and resource providers must already be registered. Keep CI disabled
until step 6. Do not use the generic `infrastructure/terraform/bootstrap` module for this procedure:
this state account is created outside Terraform and must never be imported into either stack.

**1. needs owner login — state account and protection**

```bash
set -euo pipefail
if [ ! -f "$HOME/.starter-infra-ci.env" ]; then
  umask 077
  suffix=$(openssl rand -hex 3)
  cat > "$HOME/.starter-infra-ci.env" <<EOF
SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
FRG=rg-swastarter-dev
SRG=rg-swastarter-dev
SA=ststartertfstate${suffix}
LOC=eastus2
REPO=quaz579/blazor-swa-starter
EOF
fi
source "$HOME/.starter-infra-ci.env"
test "$FRG" = rg-swastarter-dev
SRG="$FRG"
printf 'SRG=%s\n' "$SRG" >> "$HOME/.starter-infra-ci.env"
az account set --subscription "$SUB"
az group show -n "$SRG" --query id -o tsv >/dev/null
az storage account create -n "$SA" -g "$SRG" -l "$LOC" --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false --allow-shared-key-access false --https-only true -o none
az storage account blob-service-properties update -n "$SA" -g "$SRG" --enable-versioning true --enable-delete-retention true --delete-retention-days 30 --enable-container-delete-retention true --container-delete-retention-days 30 -o none
az storage container-rm create --storage-account "$SA" -g "$SRG" -n tfstate -o none
STATE_ID=$(az storage account show -n "$SA" -g "$SRG" --query id -o tsv)
OWNER_ID=$(az ad signed-in-user show --query id -o tsv)
az role assignment create --assignee-object-id "$OWNER_ID" --assignee-principal-type User --role 'Storage Blob Data Owner' --scope "$STATE_ID" -o none
az lock create -n starter-tfstate-nodelete --lock-type CanNotDelete -g "$SRG" --resource-name "$SA" --resource-type Microsoft.Storage/storageAccounts -o none
```

The backend uses native blob lease locking over Azure AD auth; no shared key is required.
Wait for RBAC propagation before migration; rerun a failed operation only after checking its result.

**2. needs owner login — two managed identities and federated credentials**

```bash
source "$HOME/.starter-infra-ci.env"
az identity create -g "$SRG" -n id-starter-infra-plan -o none
az identity create -g "$SRG" -n id-starter-infra-apply -o none
az identity federated-credential create -g "$SRG" --identity-name id-starter-infra-plan -n github-pr --issuer https://token.actions.githubusercontent.com --subject 'repo:quaz579/blazor-swa-starter:pull_request' --audiences api://AzureADTokenExchange -o none
az identity federated-credential create -g "$SRG" --identity-name id-starter-infra-apply -n github-main --issuer https://token.actions.githubusercontent.com --subject 'repo:quaz579/blazor-swa-starter:ref:refs/heads/main' --audiences api://AzureADTokenExchange -o none
printf 'PLAN_ID=%s\n' "$(az identity show -g "$SRG" -n id-starter-infra-plan --query clientId -o tsv)" >> "$HOME/.starter-infra-ci.env"
printf 'PLAN_PID=%s\n' "$(az identity show -g "$SRG" -n id-starter-infra-plan --query principalId -o tsv)" >> "$HOME/.starter-infra-ci.env"
printf 'APPLY_ID=%s\n' "$(az identity show -g "$SRG" -n id-starter-infra-apply --query clientId -o tsv)" >> "$HOME/.starter-infra-ci.env"
printf 'APPLY_PID=%s\n' "$(az identity show -g "$SRG" -n id-starter-infra-apply --query principalId -o tsv)" >> "$HOME/.starter-infra-ci.env"
```

No GitHub Environment is required: the apply credential's exact subject trusts main only.
Anyone with repository write access can execute code with the plan identity through a same-repo PR.
That identity can read keys; keep collaborators trusted and protect main. Fork PRs receive no CI OIDC token or secrets.

**3. needs owner login — plan RBAC**

```bash
source "$HOME/.starter-infra-ci.env"
FRG_ID="/subscriptions/$SUB/resourceGroups/$FRG"
STATE_C="$(az storage account show -n "$SA" -g "$SRG" --query id -o tsv)/blobServices/default/containers/tfstate"
role_file=$(mktemp)
cat > "$role_file" <<EOF
{"Name":"Starter Infra Plan Secret Reader","IsCustom":true,"Description":"Read secret values required by azurerm refresh in the starter RG.","Actions":["Microsoft.Storage/storageAccounts/listKeys/action","Microsoft.Web/staticSites/listSecrets/action","Microsoft.Web/staticSites/listAppSettings/action","Microsoft.OperationalInsights/workspaces/sharedKeys/action"],"NotActions":[],"AssignableScopes":["$FRG_ID"]}
EOF
if [ "$(az role definition list --name 'Starter Infra Plan Secret Reader' --query 'length(@)' -o tsv)" = 0 ]; then
  az role definition create --role-definition "@$role_file" -o none
else
  az role definition update --role-definition "@$role_file" -o none
fi
rm -f "$role_file"
az role assignment create --assignee-object-id "$PLAN_PID" --assignee-principal-type ServicePrincipal --role Reader --scope "$FRG_ID" -o none
az role assignment create --assignee-object-id "$PLAN_PID" --assignee-principal-type ServicePrincipal --role 'Starter Infra Plan Secret Reader' --scope "$FRG_ID" -o none
az role assignment create --assignee-object-id "$PLAN_PID" --assignee-principal-type ServicePrincipal --role 'Storage Blob Data Reader' --scope "$STATE_C" -o none
```

Validate the RG-scoped role against the installed provider during the first real plan. If refresh
fails, diagnose the denied operation and add only its specific missing read action to this custom
role at RG scope, never subscription scope. Keep all assignments within `rg-swastarter-dev`.

UNVERIFIED: azurerm_static_web_app refresh is expected to call listAppSettings as well as listSecrets;
both actions are included. Application Insights component GET is expected to return its instrumentation
key and connection string, so no additional list action is included. These refresh permissions still
need verification in the first authenticated plan.

**4. needs owner login — apply RBAC**

The app RG must already exist: it was created by the owner's manual apply, not by CI bootstrap.

```bash
source "$HOME/.starter-infra-ci.env"
az group show -n "$FRG" --query id -o tsv >/dev/null
FRG_ID="/subscriptions/$SUB/resourceGroups/$FRG"
STATE_C="$(az storage account show -n "$SA" -g "$SRG" --query id -o tsv)/blobServices/default/containers/tfstate"
az role assignment create --assignee-object-id "$APPLY_PID" --assignee-principal-type ServicePrincipal --role Contributor --scope "$FRG_ID" -o none
az role assignment create --assignee-object-id "$APPLY_PID" --assignee-principal-type ServicePrincipal --role 'Storage Blob Data Contributor' --scope "$STATE_C" -o none
```

Apply has no role-assignment rights (no Owner or User Access Administrator). Contributor on
rg-swastarter-dev also permits changes to the state account; the owner accepts this. Blob lease
locking, versioning, soft delete and the account CanNotDelete lock mitigate that access. The root
stack never manages the state account, and its CanNotDelete lock makes a group delete fail.

**5. needs owner login — back up, migrate authoritative state and verify no changes**

Finish PR #18's manual apply with your own Azure/GitHub login before continuing.
Adjust STATE_DIR below to the directory containing the authoritative existing local state and updated configuration.
Do not migrate a new checkout's empty state. Backups contain secrets: keep them private, never commit or upload them.
Run in the same Bash terminal as step 6. When prompted, enter the JSON list matching your real allowed_origins;
leave blank only if the existing state uses the module default. This becomes STARTER_INFRA_ALLOWED_ORIGINS.

```bash
source "$HOME/.starter-infra-ci.env"
STATE_DIR="$HOME/Code/blazor-swa-starter/infrastructure/terraform"
cd "$STATE_DIR"
test -s terraform.tfstate
unset STARTER_ZERO_DIFF_VERIFIED
for variable in "${!TF_VAR_@}"; do unset "$variable"; done
export ARM_SUBSCRIPTION_ID="$SUB" ARM_TENANT_ID="$TENANT" ARM_USE_OIDC=false ARM_USE_AZUREAD=true ARM_RESOURCE_PROVIDER_REGISTRATIONS=none
unset ARM_CLIENT_ID GITHUB_TOKEN GH_TOKEN TF_CLI_ARGS TF_CLI_ARGS_plan
export TF_VAR_project_name=swastarter TF_VAR_environment=dev TF_VAR_location=eastus2 TF_VAR_github_owner=quaz579 TF_VAR_github_repository=blazor-swa-starter
printf 'allowed_origins JSON (blank for module default): '
IFS= read -r ALLOWED_ORIGINS
unset TF_VAR_allowed_origins
if [ -n "$ALLOWED_ORIGINS" ]; then
  ALLOWED_ORIGINS=$(printf '%s' "$ALLOWED_ORIGINS" | jq -ce 'if type == "array" and all(.[]; type == "string") then . else error("expected JSON list of strings") end')
  export TF_VAR_allowed_origins="$ALLOWED_ORIGINS"
fi
umask 077
backup=$(mktemp -d "$HOME/starter-state-backup.$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")
cp terraform.tfstate "$backup/terraform.tfstate"
if [ -f terraform.tfstate.backup ]; then cp terraform.tfstate.backup "$backup/"; fi
cat > backend_override.tf <<'BACKEND'
terraform {
  backend "azurerm" {
    use_azuread_auth = true
    use_oidc         = true
  }
}
BACKEND
tofu init -migrate-state -force-copy -backend-config="resource_group_name=$SRG" -backend-config="storage_account_name=$SA" -backend-config=container_name=tfstate -backend-config=key=blazor-swa-starter.tfstate -backend-config=use_oidc=false
(
  set -euo pipefail
  shopt -s nullglob
  tfvars=( *.tfvars *.tfvars.json )
  for f in "${tfvars[@]}"; do cp "$f" "$backup/"; done
  restore() { for f in "${tfvars[@]}"; do cp "$backup/$f" "$f"; done; }
  trap restore EXIT
  for f in "${tfvars[@]}"; do rm "$f"; done
  set +e
  tofu plan -input=false -no-color -detailed-exitcode -exclude=github_actions_secret.swa_deploy_token
  status=$?
  set -e
  if [ "$status" -ne 0 ]; then
    printf 'STOP: pre-enable plan must show No changes (exit 0); got %s. Reconcile CI variables with authoritative settings first.\n' "$status" >&2
    exit 1
  fi
)
export STARTER_ZERO_DIFF_VERIFIED=true
```

Expect "No changes" and exit 0 before continuing.
The migration temporarily overrides use_oidc=false for the owner's Azure CLI login; CI uses OIDC=true.
The zero-diff check temporarily removes all tfvars and restores them on exit, so it checks CI's actual inputs.
Run this plan privately: it can show sensitive diagnostics. Keep local backups even after migration.
If the SWA is replaced, CI never refreshes AZURE_STATIC_WEB_APPS_API_TOKEN. Use the following
manual recovery after reviewing the destructive changes; do not run it as part of bootstrap.

**5a. needs owner login — OPTIONAL destructive manual apply / SWA secret refresh**

```bash
source "$HOME/.starter-infra-ci.env"
cd "$STATE_DIR"
GITHUB_TOKEN=$(gh auth token --user quaz579) tofu apply
```

No -exclude is used for manual recovery: the GitHub provider refreshes the deploy-token secret.
The state account's CanNotDelete lock still makes a resource-group delete fail.
After recovery, repeat step 5's zero-diff check before enabling or resuming CI.

**6. needs owner login — repository variables, on-switch LAST**

Only continue after step 5 succeeds. No secrets are created. Every gh command selects quaz579 explicitly.

```bash
source "$HOME/.starter-infra-ci.env"
test "${STARTER_ZERO_DIFF_VERIFIED:-}" = true
for kv in \
  "AZURE_TENANT_ID=$TENANT" \
  "AZURE_SUBSCRIPTION_ID=$SUB" \
  "STARTER_INFRA_PLAN_CLIENT_ID=$PLAN_ID" \
  "STARTER_INFRA_APPLY_CLIENT_ID=$APPLY_ID" \
  "STARTER_INFRA_STATE_RG=$SRG" \
  "STARTER_INFRA_STATE_ACCOUNT=$SA" \
  'STARTER_INFRA_STATE_CONTAINER=tfstate' \
  'STARTER_INFRA_STATE_KEY=blazor-swa-starter.tfstate' \
  'STARTER_INFRA_PROJECT_NAME=swastarter' \
  'STARTER_INFRA_ENVIRONMENT=dev' \
  'STARTER_INFRA_LOCATION=eastus2'; do
  GH_TOKEN=$(gh auth token --user quaz579) gh variable set "${kv%%=*}" -R quaz579/blazor-swa-starter --body "${kv#*=}"
done
GH_TOKEN=$(gh auth token --user quaz579) gh variable set STARTER_INFRA_ALLOWED_ORIGINS -R quaz579/blazor-swa-starter --body "${ALLOWED_ORIGINS:-}"
GH_TOKEN=$(gh auth token --user quaz579) gh variable set STARTER_INFRA_CI -R quaz579/blazor-swa-starter --body true
```

**7. needs owner login — OPTIONAL branch protection requiring stable check name Infra plan**

The job name is stable: Infra plan. Path-filtered workflows do not report a check on unrelated PRs:
requiring it globally can leave them waiting. Fork/Dependabot PRs are skipped. A destructive PR fails
Infra plan and needs an owner bypass to merge if required. Prefer a path-aware ruleset/check policy.
The following adds the context to EXISTING branch protection without replacing its other rules.
If main has no protection yet, configure a path-aware ruleset in GitHub instead.

```bash
source "$HOME/.starter-infra-ci.env"
printf '{"contexts":["Infra plan"]}\n' | GH_TOKEN=$(gh auth token --user quaz579) gh api --method POST -H 'Accept: application/vnd.github+json' repos/quaz579/blazor-swa-starter/branches/main/protection/required_status_checks/contexts --input - >/dev/null
```

## Teardown

```sh
cd infrastructure/terraform
terraform destroy
```

The resource group also holds the external CI state account and managed identities, and the account's
CanNotDelete lock makes a full destroy fail. For deliberate decommissioning, first back up/migrate state
out and preserve or relocate those external resources. Routine CI never removes the lock.

After deliberate teardown, `terraform apply` re-provisions app resources (a random
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
