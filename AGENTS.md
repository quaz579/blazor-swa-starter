# AGENTS.md

This is the agent contract for `blazor-swa-starter`. Read it before touching anything. It is
the canonical description of how this repo works — not a summary of one; if something here
looks stale, fix it here rather than writing a second doc that disagrees with it.

## What this is

A Blazor WebAssembly PWA + an Azure Functions **isolated-worker** managed API, deployed to
**Azure Static Web Apps (Free tier)**, provisioned end-to-end by Terraform. The domain is
deliberately generic (`Item`, a JSON blob per item) so the shape generalizes to a real feature.
It exists so an agent can clone it and stand up a deployed app unattended — see `DEPLOY.md`.

## Dependency graph — one direction only

```
App.Core  ◄──  App.Api
App.Core  ◄──  App.Web
```

`App.Core` (models + `IBlobJsonStore<T>`) has zero `ProjectReference`s — it never references
`App.Api` or `App.Web`. Verify this yourself by reading `src/App.Core/App.Core.csproj` rather
than assuming it's still true after a change. `App.Api` and `App.Web` each reference `App.Core`
only; `App.Tests` is the one project allowed to reference all three.

## Task → agent routing

Route work to the agent whose lane it falls in. Each agent's own file (`.claude/agents/<name>.md`)
is the authority on its hard rules — this table is just the dispatch.

| When the task is... | Delegate to |
|---|---|
| A new vertical feature: a new domain model, a new Functions endpoint, a new Blazor page/component, "add a CRUD endpoint for X" — with tests at every layer it touches | `.claude/agents/feature-builder.md` |
| Writing or fixing xunit/bUnit tests, a flaky unit/component test, swapping an assertion or mocking library, measuring test coverage | `.claude/agents/test-engineer.md` |
| Playwright specs or page objects, seeding Azurite fixtures, starting/stopping the local full-stack dev environment, browser-verifying a deployed PR preview or live URL | `.claude/agents/e2e-validator.md` |
| Provisioning Azure resources, Terraform (`main.tf`/`variables.tf`/`outputs.tf`/`github.tf`/`bootstrap/`), a GitHub Actions workflow, `staticwebapp.config.json`, `swa-cli.config.json`, the devcontainer, a deployment token/secret, PR preview-environment mechanics | `.claude/agents/swa-infra.md` |
| Bumping a `.csproj` TargetFramework, upgrading/pinning a NuGet package, creating or editing `global.json`, triaging a bump's build errors, reviewing a package's breaking changes before a major-version adoption | `.claude/agents/dotnet-upgrader.md` |
| Updating or reconciling root documentation, fixing stale/contradictory setup or deploy instructions, verifying a documented command still runs, removing PR-summary-style narration that leaked into a doc | `.claude/agents/docs-curator.md` |

A cold agent should be able to route correctly from this table alone — that is the point of
having it here instead of leaving routing to guesswork.

## Build / test / run — every command below was run in this repo and observed to work

| Command | What it does |
|---|---|
| `dotnet restore BlazorSwaStarter.sln` | Restore NuGet packages for all four projects |
| `dotnet build BlazorSwaStarter.sln -c Release --no-restore` | Build Core, Api, Web, Tests |
| `dotnet test BlazorSwaStarter.sln -c Release --no-build` | Run the xunit/bUnit suite (Azurite-backed tests skip cleanly if Azurite isn't up) |
| `./scripts/start-local.sh` ... `./scripts/stop-local.sh` | Bring up Azurite + Functions host + Blazor dev server + SWA CLI proxy for interactive dev; tear down only what it started |
| `./scripts/start-e2e.sh` ... `(cd tests && npm test)` ... `./scripts/stop-e2e.sh` | Same stack, E2E app-settings, then the full Playwright suite |
| `cd tests && npm ci` | Install Playwright/test dependencies |
| `cd tests && npx playwright install --with-deps chromium` | Install the Chromium browser Playwright drives |
| `cd tests && npx playwright test --grep @smoke` | Run only the read-only smoke subset |
| `bash scripts/validate-environment.sh` | Preflight: tool versions, solution build, free ports, `swa-cli.config.json`/`ports.env` drift, Playwright browser cache |
| `cd infrastructure/terraform && terraform init / validate / fmt -check -recursive / plan` | Read-only Terraform checks (see `DEPLOY.md` for `apply`) |

See `LOCAL-DEV.md` and `TESTING.md` for the full context around each of these.

## TDD expectation and test naming

Every layer a change touches gets a test at that layer before the change is considered done —
`feature-builder`'s hard rule is that a slice with no test at a touched layer is incomplete
even if everything else is green. `test-engineer`'s hard rule is the other half: a red test
gets fixed by fixing whichever side is actually wrong (test or app), stated explicitly as a
verdict — never by weakening an assertion, skipping the test, or loosening a match to make it
pass.

Test methods in `src/App.Tests/` follow `MethodName_Scenario_ExpectedBehavior`, e.g.
`GetItems_NoItemsStored_Returns200WithEmptyArray`, `CreateItem_NameExceeds100Chars_Returns400WithErrorBody`.
Match this convention for new tests; don't invent a different shape.

## Where things live

```
blazor-swa-starter/
├── src/App.Core/               domain models + IBlobJsonStore<T> — no Azure SDK, no ProjectReferences
├── src/App.Api/                Azure Functions isolated-worker app; BlobJsonStore<T> impl; the managed API
├── src/App.Web/                Blazor WebAssembly PWA
├── src/App.Tests/               xunit + bUnit + NSubstitute + AwesomeAssertions, mirroring Core/Api/Web
├── tests/                      Playwright E2E: playwright.config.ts, pages/, specs/, helpers/ (incl. the Azurite fixture seeder)
├── scripts/                    ports.env plus start/stop for the local and e2e stacks, and validate-environment.sh
├── infrastructure/terraform/    provisions every Azure resource and writes the GitHub Actions deploy-token secret
├── .claude/agents/              the six agents this file routes to
├── .devcontainer/               devcontainer.json + setup.sh
└── .github/workflows/            ci.yml, pr-cleanup.yml, copilot-setup-steps.yml
```

## The one canonical doc rule

There are exactly seven markdown files in this repo: `README.md`, `AGENTS.md`, `DEPLOY.md`,
`LOCAL-DEV.md`, `TESTING.md`, `CLAUDE.md` (one line, `@AGENTS.md`), and
`.github/copilot-instructions.md` (a pointer at this file). Every topic has exactly one home —
deployment lives in `DEPLOY.md`, testing in `TESTING.md`, and so on. **New work updates the
canonical doc for its topic; it never adds a new markdown file.** A prior version of this
starter had 17 root markdown files with three mutually contradictory deployment stories; the
fix was collapsing them, not writing an 18th. If you find yourself wanting to add
`docs/something.md` or a `README` inside a subdirectory, put that content in the doc that
already owns the topic instead.

## How to prove your change works

Three tiers, in order, each cheaper to run than the next:

1. **Local unit tests.** `dotnet test BlazorSwaStarter.sln -c Release`. Fast, no external
   services required (Azurite-gated tests skip cleanly without an emulator).
2. **Local E2E through the SWA CLI proxy.** `./scripts/start-e2e.sh`, then `cd tests && npm test`,
   then `./scripts/stop-e2e.sh` — always the pair, including on failure. This is a full
   Azurite + Functions + Blazor + SWA-proxy stack, and it is what CI's `e2e` job runs.
3. **The PR preview environment.** Every PR gets a live Static Web Apps preview slot deployed
   by CI. **A preview environment inherits the production Static Web App's app settings, and
   therefore production storage** — it is not an isolated sandbox. Only the read-only `@smoke`
   subset (`tests/specs/smoke.spec.ts`) may run against one; CI's `smoke-preview` job enforces
   this automatically, and you must not run the full suite or any write path (`POST`/`DELETE`
   against `/api/items`) against a preview or live URL by hand.

## Ports: single source of truth

`scripts/ports.env` is the *only* place port numbers are written. Every shell script sources it;
`tests/helpers/ports.ts` parses it for Playwright. `swa-cli.config.json` is static JSON and
cannot read environment variables, so it deliberately duplicates the same port values — and
`scripts/validate-environment.sh` asserts the two agree, failing loudly on drift. That check is
the reason the duplication is acceptable: never hand-edit one without the other, and never
hardcode a port anywhere else.
