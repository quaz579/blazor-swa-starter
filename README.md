# blazor-swa-starter

Blazor WebAssembly PWA + an Azure Functions isolated-worker managed API, deployed to Azure
Static Web Apps (Free tier) and provisioned end-to-end by Terraform. .NET version comes from
[`global.json`](global.json).

The deployed demo gates writes (`POST`/`DELETE` on `/api/items`) behind a logged-in user via
route rules in `staticwebapp.config.json`, with no application code involved — see
[`DEPLOY.md`](DEPLOY.md#write-auth). Removing those two route rules is the first step to
opening the API up for your own use.

## Run it locally

Prerequisites: the .NET SDK `global.json` pins, Node 22.x, [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4,
the [SWA CLI](https://azure.github.io/static-web-apps-cli/) (`npm i -g @azure/static-web-apps-cli`), and Azurite >= 3.37.0.

```sh
./scripts/start-local.sh          # Azurite + Functions host + Blazor dev server + SWA CLI proxy
# open http://localhost:4280
./scripts/stop-local.sh           # tears down only what start-local.sh started
```

See [`LOCAL-DEV.md`](LOCAL-DEV.md) for what each script does and the exact ports.

## Deploy it

```sh
gh repo create <owner>/<repo> --public --source=.
cd infrastructure/terraform
export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
export GITHUB_TOKEN=$(gh auth token)
terraform init && terraform apply
git push -u origin main
```

Terraform provisions the Static Web App, storage account, and Application Insights, and writes
the deployment token to the GitHub repo as an Actions secret — CI then builds and deploys on
every push to `main`. Full sequence, the ordering wrinkle if you push before `apply`, and
teardown: [`DEPLOY.md`](DEPLOY.md).

## Tests

```sh
dotnet test BlazorSwaStarter.sln -c Release
```

Unit, component, integration, and E2E tiers, and how to run each: [`TESTING.md`](TESTING.md).

## More

- [`AGENTS.md`](AGENTS.md) — the agent contract: dependency graph, which `.claude/agents/`
  role owns what, the verified command table, and the doc-set rule.
- [`LOCAL-DEV.md`](LOCAL-DEV.md) — running the full stack locally.
- [`DEPLOY.md`](DEPLOY.md) — provisioning, CI/CD, and teardown.
- [`TESTING.md`](TESTING.md) — the test pyramid.
