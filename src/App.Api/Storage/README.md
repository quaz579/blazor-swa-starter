# Versioned JSON blob store

Use `IVersionedBlobJsonStore<T>` when concurrent writers must not silently overwrite each other.
The store uses Azure Blob Storage ETags as opaque tokens and keeps the existing camelCase JSON
serialization, storage resolver, prefix, and reset write gate.

    public sealed class CustomerRepository(IVersionedBlobJsonStore<Customer> customers)
    {
        public async Task<VersionedWriteResult> CreateAsync(Customer customer, CancellationToken ct) =>
            await customers.TryCreateAsync(customer.Id, customer, ct);

        public async Task<VersionedWriteResult> ReplaceAsync(
            Customer customer,
            string expectedETag,
            CancellationToken ct) =>
            await customers.TryReplaceAsync(customer.Id, customer, expectedETag, ct);
    }

GetVersionedAsync(key) returns `Versioned<T>` with the decoded value and its ETag, or null when
the blob or container is missing. TryCreateAsync uses create-only conditions.
TryReplaceAsync requires a specific, non-empty ETag from a prior read; null, empty, whitespace,
and `*` values are rejected. Both return VersionedWriteResult.Success with the new ETag, or
VersionedWriteResult.Conflict when Azure returns 409 or 412. Other storage
and JSON failures propagate. The store does not retry a conflict; reread and recompute only when
the domain operation is safe to retry.

Register both storage interfaces together as usual:

    services.AddBlobJsonStore<Customer>("customers");

Resolve `IBlobJsonStore<Customer>` for unconditional operations or
`IVersionedBlobJsonStore<Customer>` for optimistic concurrency. Both interfaces resolve to the
same singleton. There are no new storage settings; local Azurite and deployed storage use the
existing storage connection and identity rules. Writes participate in ResetGate.

## Configuration and deployment

The versioned store adds no configuration keys. Local development continues to use the existing
UseDevelopmentStorage=true Azurite default. Production uses the configured storage account and
identity checks described in DEPLOY.md. If stored values also contain protected fields, use
[Security/README.md](../Security/README.md) for production key generation with openssl rand
-base64 32, app settings, and rotation.

## Testing

BlobJsonStoreAzuriteTests contains real emulator checks for create collisions, missing blobs,
stale replacement, changing ETags, and two writers using one ETag. Run with Azurite on ports
10000–10002 to execute them. For tests that also encrypt a stored field, use FieldEncryptionTestKit
from src/App.Tests/Security/FieldEncryptionTestKit.cs; see the
[Security README](../Security/README.md) for its examples.
