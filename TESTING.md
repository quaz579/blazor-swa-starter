# Testing

Four tiers, all in `src/App.Tests/` except the last:

| Tier | What | Where |
|---|---|---|
| Unit (xunit) | `App.Core` models, `App.Api` Functions with mocked `IBlobJsonStore<T>` (NSubstitute) | `Models/`, `Functions/` |
| Component (bUnit) | Blazor pages/components, HTTP mocked via `DelegateHttpMessageHandler` | `Web/` |
| Integration (real Azurite) | `BlobJsonStore<T>` against a real blob emulator, no mocked `BlobServiceClient` | `Storage/BlobJsonStoreAzuriteTests.cs` |
| E2E (Playwright) | Full stack through the SWA CLI proxy, real browser | `tests/specs/` |

## Unit + component + integration

```sh
dotnet test BlazorSwaStarter.sln -c Release
```

Runs all three .NET tiers together (bUnit tests build on the xunit runner). The integration
tier's tests are attributed `[AzuriteFact]` (`src/App.Tests/Storage/AzuriteFactAttribute.cs`), a
custom `FactAttribute` that probes `127.0.0.1:10000` and **skips — not fails —** when nothing is
listening there, so a bare `dotnet test` with no emulator running reports those tests as
skipped rather than failed. Start a standalone Azurite first
(`npx azurite@^3.37.0 --location <dir> --blobPort 10000 --queuePort 10001 --tablePort 10002`) to
have that tier actually execute.

Each `BlobJsonStoreAzuriteTests` instance uses a unique `test-<guid>` blob-path prefix and
cleans up only the keys it created in `DisposeAsync`, so tests never collide and a bare
`dotnet test` never needs Azurite up at all — it just skips that tier.

## Test naming and TDD

Method names follow `MethodName_Scenario_ExpectedBehavior`
(`GetItems_NoItemsStored_Returns200WithEmptyArray`,
`CreateItem_NameExceeds100Chars_Returns400WithErrorBody`). Every layer a change touches gets a
test at that layer as part of the change, not after. A failing test gets fixed by fixing
whichever side is actually wrong — never by weakening an assertion, adding `Skip`, or loosening
a match; see `.claude/agents/test-engineer.md` for the full hard rules this repo holds tests to.

## E2E (Playwright)

```sh
./scripts/start-e2e.sh
cd tests && npm ci && npm test        # every spec under tests/specs/
./scripts/stop-e2e.sh                 # always pair with start, including on failure
```

Targets the SWA CLI proxy (`http://localhost:4280` from `scripts/ports.env`, resolved in
`tests/helpers/ports.ts` — never hardcode a port in a spec), which is what actually routes
`/api/*` to the Functions host; the Blazor dev server port alone does not. `tests/specs/` covers
CRUD flows, validation, empty/error states, and a guard spec
(`azurite-seed-guard.spec.ts`) that proves the fixture seeder refuses any connection string
other than `UseDevelopmentStorage=true`.

Run only the read-only smoke subset (the only tests safe against a deployed preview or live
URL — see `AGENTS.md`'s "how to prove your change works" and `DEPLOY.md`'s PR-preview section):

```sh
cd tests && npx playwright test --grep @smoke
```

First run on a machine needs the browser once: `cd tests && npx playwright install --with-deps chromium`.
