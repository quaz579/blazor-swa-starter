# Copilot instructions

## Building blocks — use these, never write your own

- Use IFieldProtector with a stable purpose string for sensitive fields; do not hand-roll
  cryptography or add product-specific encryption-key settings. See
  [src/App.Api/Security/README.md](../src/App.Api/Security/README.md).
- Use IVersionedBlobJsonStore&lt;T&gt; for optimistic concurrency and the registered TimeProvider
  for time-sensitive behavior. See
  [src/App.Api/Storage/README.md](../src/App.Api/Storage/README.md) and AGENTS.md.

The agent contract for this repo lives in [`AGENTS.md`](../AGENTS.md) at the repo root. Read
it before making any change — it covers the dependency graph, which `agents/` role
owns which kind of task, the verified command table, the test-naming convention, and the
one-canonical-doc rule.

Don't duplicate that content here. If something about working in this repo needs to change,
change it in `AGENTS.md`.
