# Local development

## Prerequisites

- .NET SDK matching [`global.json`](global.json)
- Node 22.x
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4 (`func`)
- [SWA CLI](https://azure.github.io/static-web-apps-cli/) (`npm i -g @azure/static-web-apps-cli`)
- Azurite **>= 3.37.0** — `Azure.Storage.Blobs` 12.29.2 (used by `App.Api`) negotiates Storage
  REST API version `2026-06-06`; older Azurite builds reject that version and every blob call
  fails. The start scripts pin this for you (`npx azurite@^3.37.0`), so it matters mainly if you
  run a standalone Azurite yourself.

Check all of this at once:

```sh
bash scripts/validate-environment.sh
```

It checks tool versions, that the solution builds, that the dev ports are free, that
`swa-cli.config.json` agrees with `scripts/ports.env`, and that a Playwright Chromium browser is
cached — and starts nothing itself. **Known false-fail:** the last check globs both
`~/Library/Caches/ms-playwright` (macOS) and `~/.cache/ms-playwright` (Linux) with `find`, and
under the script's `set -o pipefail` a nonexistent path makes `find` exit non-zero and the whole
check report `FAIL` even when Chromium is installed and cached under whichever of the two paths
actually exists on your OS. Observed on macOS in this session: `FAIL: no Playwright chromium
browser cache found` with Chromium already present under `~/Library/Caches/ms-playwright/`. If
you hit this, confirm directly — `ls ~/Library/Caches/ms-playwright/` (or the Linux path) and
look for a `chromium-*` entry — before assuming Playwright isn't installed.

## Running the stack

```sh
./scripts/start-local.sh
# open http://localhost:4280 — the SWA CLI proxy, not the Blazor dev server directly
./scripts/stop-local.sh
```

`start-local.sh` starts Azurite, the Functions host (`func start`), the Blazor dev server
(`dotnet run`), and the SWA CLI proxy, in that order, waiting for each to actually respond before
starting the next. It's idempotent — a service already listening on its port is left alone — and
detaches on success. `stop-local.sh` kills only the PIDs `start-local.sh` recorded (plus each
one's forked child that ends up holding the actual listening socket), never a blanket
process-name kill. Logs land in `.azurite/logs/*-local.log`.

For the E2E variant (same stack, `ASPNETCORE_ENVIRONMENT=E2E` so the Blazor app loads
`wwwroot/appsettings.E2E.json` instead of `appsettings.Development.json`), use
`./scripts/start-e2e.sh` / `./scripts/stop-e2e.sh` — see [`TESTING.md`](TESTING.md) for running
tests against it.

## Ports

Defined once in [`scripts/ports.env`](scripts/ports.env) — never hardcode one elsewhere:

| Service | Port |
|---|---|
| SWA CLI proxy (what you open/test against) | 4280 |
| Blazor dev server | 5158 |
| Functions host | 7071 |
| Azurite blob | 10000 |
| Azurite queue | 10001 |
| Azurite table | 10002 |

`swa-cli.config.json` is static JSON and can't source `ports.env`, so it duplicates the same
numbers; `scripts/validate-environment.sh` asserts the two agree.

## Storage connection

`App.Api`'s `BlobJsonStore<T>` resolves its connection string in order:
`AZURE_STORAGE_CONNECTION_STRING` → `AzureWebJobsStorage` → `UseDevelopmentStorage=true`. Locally
this always lands on the Azurite connection string; `src/App.Api/local.settings.json` is
deliberately committed (it holds only `UseDevelopmentStorage=true`) so a fresh clone runs with
zero setup.
