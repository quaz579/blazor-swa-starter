using App.Api.Storage;
using App.Core.Models;
using App.Core.Storage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Storage;

/// <summary>
/// Real round-trip against Azurite (no mocked <c>BlobServiceClient</c>). Skips cleanly via
/// <see cref="AzuriteFactAttribute"/> when Azurite is not running. Each instance gets a unique
/// blob-path prefix, and xUnit constructs a fresh instance per test method, so tests never see
/// each other's data and teardown only ever deletes what this instance itself created.
/// </summary>
public sealed class BlobJsonStoreAzuriteTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private readonly BlobJsonStore<Item> _store = new(
        ConnectionString,
        new BlobJsonStoreOptions<Item> { Prefix = $"test-{Guid.NewGuid():N}" },
        NullLogger<BlobJsonStore<Item>>.Instance);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var keys = await _store.ListKeysAsync();
        foreach (var key in keys)
        {
            await _store.DeleteAsync(key);
        }
    }

    [AzuriteFact]
    public async Task PutAsync_ThenGetAsync_ReturnsStoredItem()
    {
        // Arrange
        var item = new Item { Id = "abc123", Name = "Widget", Description = "A widget", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") };

        // Act
        await _store.PutAsync(item.Id, item);
        var retrieved = await _store.GetAsync(item.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(item.Id);
        retrieved.Name.Should().Be(item.Name);
        retrieved.Description.Should().Be(item.Description);
        retrieved.CreatedAt.Should().Be(item.CreatedAt);
    }

    [AzuriteFact]
    public async Task GetAsync_KeyDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync($"missing-{Guid.NewGuid():N}");

        // Assert
        result.Should().BeNull();
    }

    [AzuriteFact]
    public async Task PutAsync_ExistingKey_OverwritesStoredValue()
    {
        // Arrange
        const string key = "overwrite-me";
        var original = new Item { Id = key, Name = "Original", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") };
        var updated = new Item { Id = key, Name = "Updated", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z").AddMinutes(1) };

        // Act
        await _store.PutAsync(key, original);
        await _store.PutAsync(key, updated);
        var retrieved = await _store.GetAsync(key);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Updated");
        retrieved.CreatedAt.Should().Be(updated.CreatedAt);
    }

    [AzuriteFact]
    public async Task ListKeysAsync_WithPrefix_ReturnsOnlyMatchingKeys()
    {
        // Arrange
        await _store.PutAsync("widget-1", new Item { Id = "widget-1", Name = "W1", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") });
        await _store.PutAsync("widget-2", new Item { Id = "widget-2", Name = "W2", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") });
        await _store.PutAsync("gadget-1", new Item { Id = "gadget-1", Name = "G1", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") });

        // Act
        var keys = await _store.ListKeysAsync("widget");

        // Assert
        keys.Should().BeEquivalentTo(["widget-1", "widget-2"]);
    }

    [AzuriteFact]
    public async Task DeleteAsync_ExistingKey_RemovesItemAndReturnsTrue()
    {
        // Arrange
        const string key = "to-delete";
        await _store.PutAsync(key, new Item { Id = key, Name = "Temp", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") });

        // Act
        var deleted = await _store.DeleteAsync(key);
        var afterDelete = await _store.GetAsync(key);

        // Assert
        deleted.Should().BeTrue();
        afterDelete.Should().BeNull();
    }

    [AzuriteFact]
    public async Task DeleteAsync_KeyDoesNotExist_ReturnsFalse()
    {
        // Act
        var deleted = await _store.DeleteAsync($"never-existed-{Guid.NewGuid():N}");

        // Assert
        deleted.Should().BeFalse();
    }

    [AzuriteFact]
    public async Task TryCreateAsync_ExistingKey_ReturnsConflict()
    {
        var item = new Item { Id = "conditional-create", Name = "First" };

        var first = await _store.TryCreateAsync(item.Id, item);
        var second = await _store.TryCreateAsync(item.Id, new Item { Id = item.Id, Name = "Second" });

        var success = first.Should().BeOfType<VersionedWriteResult.Success>().Subject;
        success.ETag.Should().NotBeNullOrWhiteSpace();
        second.Should().Be(VersionedWriteResult.Conflict.Instance);
    }

    [AzuriteFact]
    public async Task TryReplaceAsync_ConcurrentWritersOnOneETag_ExactlyOneWins()
    {
        const string key = "conditional-race";
        await _store.TryCreateAsync(key, new Item { Id = key, Name = "Initial" });
        var current = await _store.GetVersionedAsync(key);
        current.Should().NotBeNull();

        var results = await Task.WhenAll(
            _store.TryReplaceAsync(key, new Item { Id = key, Name = "Writer one" }, current!.ETag),
            _store.TryReplaceAsync(key, new Item { Id = key, Name = "Writer two" }, current.ETag));

        results.Count(result => result is VersionedWriteResult.Success).Should().Be(1);
        results.Count(result => result is VersionedWriteResult.Conflict).Should().Be(1);
    }

    [AzuriteFact]
    public async Task TryReplaceAsync_StaleETag_ReturnsConflictAndKeepsPriorData()
    {
        const string key = "conditional-stale";
        var created = await _store.TryCreateAsync(key, new Item { Id = key, Name = "Initial" });
        created.Should().BeOfType<VersionedWriteResult.Success>();
        var initial = await _store.GetVersionedAsync(key);
        initial.Should().NotBeNull();
        var committed = await _store.TryReplaceAsync(key, new Item { Id = key, Name = "Committed" }, initial!.ETag);
        committed.Should().BeOfType<VersionedWriteResult.Success>();

        var stale = await _store.TryReplaceAsync(key, new Item { Id = key, Name = "Stale" }, initial.ETag);
        var after = await _store.GetVersionedAsync(key);

        stale.Should().Be(VersionedWriteResult.Conflict.Instance);
        after!.Value.Name.Should().Be("Committed");
    }

    [AzuriteFact]
    public async Task TryReplaceAsync_SuccessfulReplace_ChangesETag()
    {
        const string key = "conditional-etag";
        await _store.TryCreateAsync(key, new Item { Id = key, Name = "Initial" });
        var initial = await _store.GetVersionedAsync(key);
        initial.Should().NotBeNull();

        var replaced = await _store.TryReplaceAsync(key, new Item { Id = key, Name = "Updated" }, initial!.ETag);
        var after = await _store.GetVersionedAsync(key);

        var success = replaced.Should().BeOfType<VersionedWriteResult.Success>().Subject;
        success.ETag.Should().NotBe(initial.ETag);
        after!.ETag.Should().Be(success.ETag);
    }

    [AzuriteFact]
    public async Task GetVersionedAsync_MissingKey_ReturnsNull()
    {
        var result = await _store.GetVersionedAsync($"missing-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }
}
