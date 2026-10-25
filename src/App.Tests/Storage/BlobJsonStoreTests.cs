using System.Diagnostics;
using App.Api.Storage;
using App.Core.Models;
using AwesomeAssertions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

    [Theory]
    [InlineData("BlobNotFound")]
    [InlineData("ContainerNotFound")]
    public async Task GetAsync_BlobOrContainerMissing_ReturnsNull(string errorCode)
    {
        var blob = Substitute.For<BlobClient>();
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient("items/missing.json").Returns(blob);
        blob.ExistsAsync(Arg.Any<CancellationToken>()).Returns(Response.FromValue(true, Substitute.For<Response>()));
        blob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<BlobDownloadResult>>(new RequestFailedException(404, "Missing", errorCode, null)));
        var store = new BlobJsonStore<Item>(container,
            new BlobJsonStoreOptions<Item> { Prefix = "items" }, NullLogger<BlobJsonStore<Item>>.Instance);

        var result = await store.GetAsync("missing");

        result.Should().BeNull();
        await blob.Received(1).DownloadContentAsync(Arg.Any<CancellationToken>());
        await blob.DidNotReceive().ExistsAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(403, "AuthorizationFailure")]
    [InlineData(404, "OtherNotFound")]
    [InlineData(500, "InternalError")]
    public async Task GetAsync_UnexpectedStorageFailure_PropagatesException(int status, string errorCode)
    {
        var blob = Substitute.For<BlobClient>();
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient("items/key.json").Returns(blob);
        blob.ExistsAsync(Arg.Any<CancellationToken>()).Returns(Response.FromValue(true, Substitute.For<Response>()));
        var failure = new RequestFailedException(status, "Storage failure", errorCode, null);
        blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<Response<BlobDownloadResult>>(failure));
        var store = new BlobJsonStore<Item>(container,
            new BlobJsonStoreOptions<Item> { Prefix = "items" }, NullLogger<BlobJsonStore<Item>>.Instance);

        var act = () => store.GetAsync("key");

        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Should().BeSameAs(failure);
    }

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
