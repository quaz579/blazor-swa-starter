using System.Diagnostics;
using App.Api.Storage;
using App.Core.Models;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Storage;

/// <summary>
/// Pure key/prefix-validation tests. These never touch the network — every
/// <see cref="BlobJsonStore{T}"/> method validates before any storage I/O, so these run
/// deterministically fast whether or not Azurite is up. The real round-trip lives in
/// <see cref="BlobJsonStoreAzuriteTests"/>.
/// </summary>
public sealed class BlobJsonStoreTests
{
    private readonly BlobJsonStore<Item> _store = new(
        "UseDevelopmentStorage=true",
        new BlobJsonStoreOptions<Item> { Prefix = "items" },
        NullLogger<BlobJsonStore<Item>>.Instance);

    [Fact]
    public async Task GetAsync_KeyIsNull_ThrowsArgumentException()
    {
        var act = async () => await _store.GetAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_KeyIsEmpty_ThrowsArgumentException()
    {
        var act = async () => await _store.GetAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_KeyIsWhitespace_ThrowsArgumentException()
    {
        var act = async () => await _store.GetAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("items/1")]
    [InlineData("items\\1")]
    [InlineData("..")]
    [InlineData("../../secrets")]
    [InlineData("items/../../x")]
    public async Task GetAsync_KeyContainsPathSeparatorOrTraversal_ThrowsArgumentException(string key)
    {
        var act = async () => await _store.GetAsync(key);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PutAsync_KeyContainsTraversal_FailsClosedBeforeAnyStorageIo()
    {
        // Regression guard for a fixed ordering bug: validation must run before the
        // CreateIfNotExistsAsync network call, not after it — otherwise this still throws
        // ArgumentException eventually, just slowly (or as a network exception with no backend).
        var stopwatch = Stopwatch.StartNew();

        var act = async () => await _store.PutAsync("../../secrets", new Item());

        await act.Should().ThrowAsync<ArgumentException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DeleteAsync_KeyContainsTraversal_ThrowsArgumentException()
    {
        var act = async () => await _store.DeleteAsync("../etc/passwd");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a/../../b")]
    public async Task ListKeysAsync_PrefixContainsBackslashOrTraversal_ThrowsArgumentException(string prefix)
    {
        var act = async () => await _store.ListKeysAsync(prefix);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
