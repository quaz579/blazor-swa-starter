using System.Text.Json;
using System.Text.Json.Nodes;
using App.Api.Seeding;
using App.Api.Storage;
using App.Core.Models;
using App.Tests.Storage;
using AwesomeAssertions;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Seeding;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class SeedAzuriteTests
{
    [AzuriteFact]
    public async Task Reset_StrayBusinessData_ClearsOnlyRegisteredStoresAndMatchesManifest()
    {
        var suffix = Guid.NewGuid().ToString("n");
        var tables = new TableServiceClient(StorageConnection.ResolveConnectionString());
        var blobs = new BlobServiceClient(StorageConnection.ResolveConnectionString());
        var business = tables.GetTableClient("seed" + suffix);
        var container = blobs.GetBlobContainerClient("seed-" + suffix);
        var host = blobs.GetBlobContainerClient("azure-webjobs-seed-" + suffix);
        var users = tables.GetTableClient("pocusers");
        var sessions = tables.GetTableClient("pocsessions");
        try
        {
            await business.CreateIfNotExistsAsync();
            await business.UpsertEntityAsync(new TableEntity("stray", "row"));
            await container.CreateIfNotExistsAsync();
            await container.GetBlobClient("stray").UploadAsync(BinaryData.FromString("stray"));
            await host.CreateIfNotExistsAsync();
            await host.GetBlobClient("keep").UploadAsync(BinaryData.FromString("host"));
            foreach (var table in new[] { users, sessions })
            {
                await table.CreateIfNotExistsAsync();
                await table.UpsertEntityAsync(new TableEntity(suffix, "keep") { ["Value"] = "auth" });
            }
            var tableStore = new TableSeedStore(business);
            using var service = new SeedService(new TableFixtureSeed(tableStore), [new BlobSeedStore(container), tableStore], NullLogger<SeedService>.Instance);
            var result = await service.ResetAsync(default);
            result!.SeedVersion.Should().Be("table-test");
            result.Tables.Should().BeEquivalentTo(new Dictionary<string, int> { [business.Name] = 1 });
            result.Blobs.Should().BeEquivalentTo(new Dictionary<string, int> { [container.Name] = 0 });
            result.Stores.Should().Equal($"blob:{container.Name}", $"table:{business.Name}");
            (await business.GetEntityIfExistsAsync<TableEntity>("stray", "row")).HasValue.Should().BeFalse();
            foreach (var table in new[] { users, sessions })
                (await table.GetEntityAsync<TableEntity>(suffix, "keep")).Value["Value"].Should().Be("auth");
            (await host.GetBlobClient("keep").ExistsAsync()).Value.Should().BeTrue();
        }
        finally
        {
            await business.DeleteAsync();
            await container.DeleteIfExistsAsync();
            await host.DeleteIfExistsAsync();
            foreach (var table in new[] { users, sessions }) await table.DeleteEntityAsync(suffix, "keep");
        }
    }

    [AzuriteFact]
    public async Task Reset_ItemsSample_MatchesExactManifestAndFixedRowsOnRepeat()
    {
        var container = new BlobServiceClient(StorageConnection.ResolveConnectionString()).GetBlobContainerClient("app-data");
        var store = new BlobJsonStore<Item>(container, new BlobJsonStoreOptions<Item> { Prefix = "items" }, NullLogger<BlobJsonStore<Item>>.Instance);
        using var service = new SeedService(new ItemsSeedData(store), [new BlobSeedStore(container)], NullLogger<SeedService>.Instance);
        await store.PutAsync("stray", new Item { Id = "stray", Name = "stray" });
        for (var i = 0; i < 2; i++)
        {
            var result = (await service.ResetAsync(default))!;
            using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-manifest.json")));
            using var actual = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            actual.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("seed_version", "tables", "blobs", "stores");
            foreach (var name in new[] { "seed_version", "tables", "blobs" })
                JsonNode.DeepEquals(JsonNode.Parse(actual.RootElement.GetProperty(name).GetRawText()), JsonNode.Parse(expected.RootElement.GetProperty(name).GetRawText())).Should().BeTrue($"manifest property {name} must match exactly");
            result.Stores.Should().Equal("blob:app-data");
            (await store.GetAsync("stray")).Should().BeNull();
            (await store.GetAsync("fixture-newest"))!.Name.Should().Be("Fixture Newest");
        }
    }

    [AzuriteFact]
    public async Task Reset_MissingRegisteredStores_ReturnsVersionZeroAndZeroCountsForEveryStore()
    {
        var suffix = Guid.NewGuid().ToString("n");
        var table = "seed" + suffix;
        var container = "seed-" + suffix;
        using var service = new SeedService(new EmptySeedData(), [new TableSeedStore(table), new BlobSeedStore(container)], NullLogger<SeedService>.Instance);
        var result = (await service.ResetAsync(default))!;
        result.SeedVersion.Should().Be("0");
        result.Tables.Should().BeEquivalentTo(new Dictionary<string, int> { [table] = 0 });
        result.Blobs.Should().BeEquivalentTo(new Dictionary<string, int> { [container] = 0 });
        result.Stores.Should().Equal($"blob:{container}", $"table:{table}");
    }

    [Theory]
    [InlineData("pocusers")]
    [InlineData("pocsessions")]
    [InlineData("AzureWebJobsHostLogs")]
    public void Register_ProtectedTable_RejectsRegistration(string name)
    {
        var register = () => new TableSeedStore(name);
        register.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("azure-webjobs-hosts")]
    [InlineData("$logs")]
    public void Register_SystemContainer_RejectsRegistration(string name)
    {
        var register = () => new BlobSeedStore(name);
        register.Should().Throw<ArgumentException>();
    }

    private sealed class TableFixtureSeed(TableSeedStore store) : ISeedData
    {
        public string Version => "table-test";
        public Task EnsureSeededAsync(CancellationToken cancellationToken) => store.UpsertAsync(new TableEntity("seed", "fixed"), cancellationToken);
    }
}
