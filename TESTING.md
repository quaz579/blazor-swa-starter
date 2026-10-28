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
`dotnet test` never needs Azurite up at all — it just skips that tier. CI also runs the .NET
suite after the local E2E stack starts and rejects any skipped tests, so emulator-backed checks
are required by the E2E gate.

## Test naming and TDD

Method names follow `MethodName_Scenario_ExpectedBehavior`
(`GetOrders_NoOrdersStored_Returns200WithEmptyArray`,
`CreateOrder_NameExceeds100Chars_Returns400WithErrorBody`). Every layer a change touches gets a
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

`staticwebapp.config.json` requires an authenticated session for write verbs (`POST`/`DELETE`)
on gated routes (see `DEPLOY.md`'s "Write auth"; the starter's Items sample uses `/api/items`),
and the SWA CLI emulator enforces that locally too. Specs that exercise those verbs (the Items
sample's `items-create.spec.ts` and `items-delete.spec.ts`, if present) call `loginAsAuthenticatedUser` from `tests/helpers/auth.ts` in a `beforeEach`, which sets the
emulator's `StaticWebAppsAuthCookie` directly — the same base64-JSON principal the emulator's
own `/.auth/login/<provider>` mock form would set, without driving that page. Tests tagged
`@smoke` stay unauthenticated and read-only, including anonymous requests that are rejected
without mutation. CI selects only `smoke.spec.ts` explicitly for deployed previews.

With the opt-in POC auth scaffold enabled (see `AGENTS.md`), `tests/specs/poc-auth.spec.ts`
logs in as the fixture user through `loginAs`, exercises session writes, and checks failed
sign-out and delayed session probes. `change-password.spec.ts` changes local fixture passwords
and restores them in cleanup, including after failed concurrency assertions; it refuses a
non-local target. Its anonymous rejection test runs separately without session or password
cleanup and carries `@smoke`, as does the disabled-auth check. Tests tagged `@prod`
are safe against a live product, may create sessions, and never run against a preview.
`loginAsAuthenticatedUser`
also signs in so the write specs keep working. With auth disabled,
`poc-auth-disabled.spec.ts` asserts the endpoints are inert and the auth specs skip.

Run the preview smoke subset (see `AGENTS.md`'s "how to prove your change works" and `DEPLOY.md`'s PR-preview section):

```sh
cd tests && npx playwright test specs/smoke.spec.ts --grep @smoke
```

First run on a machine needs the browser once: `cd tests && npx playwright install --with-deps chromium`.
