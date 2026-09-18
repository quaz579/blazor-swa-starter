using App.Api.Storage;
using App.Core.Models;
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
        var item = new Item { Id = "abc123", Name = "Widget", Description = "A widget", CreatedAt = DateTimeOffset.UtcNow };

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
        var original = new Item { Id = key, Name = "Original", CreatedAt = DateTimeOffset.UtcNow };
        var updated = new Item { Id = key, Name = "Updated", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1) };

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
        await _store.PutAsync("widget-1", new Item { Id = "widget-1", Name = "W1", CreatedAt = DateTimeOffset.UtcNow });
        await _store.PutAsync("widget-2", new Item { Id = "widget-2", Name = "W2", CreatedAt = DateTimeOffset.UtcNow });
        await _store.PutAsync("gadget-1", new Item { Id = "gadget-1", Name = "G1", CreatedAt = DateTimeOffset.UtcNow });

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
        await _store.PutAsync(key, new Item { Id = key, Name = "Temp", CreatedAt = DateTimeOffset.UtcNow });

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
}
