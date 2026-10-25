using App.Api.PocAuth;
using App.Api.Storage;
using App.Core.Models;
using App.Core.Storage;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Storage;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private const string ConnectionStringVariable = "AZURE_STORAGE_CONNECTION_STRING";
    private readonly string? _originalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public ServiceCollectionExtensionsTests() =>
        Environment.SetEnvironmentVariable(ConnectionStringVariable, "UseDevelopmentStorage=true");

    public void Dispose() =>
        Environment.SetEnvironmentVariable(ConnectionStringVariable, _originalConnectionString);

    [Fact]
    public void AddBlobJsonStore_RegisteredCollection_ResolvesSingletonWithConfiguredPrefix()
    {
        var services = new ServiceCollection().AddLogging();

        var result = services.AddBlobJsonStore<Item>("custom-items");
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IBlobJsonStore<Item>>();

        result.Should().BeSameAs(services);
        store.Should().BeOfType<BlobJsonStore<Item>>();
        provider.GetRequiredService<IBlobJsonStore<Item>>().Should().BeSameAs(store);
        provider.GetRequiredService<BlobJsonStoreOptions<Item>>().Prefix.Should().Be("custom-items");
    }

    [Fact]
    public void AddBlobJsonStore_DifferentModelTypes_KeepsStoresAndPrefixesSeparate()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddBlobJsonStore<Item>("items")
            .AddBlobJsonStore<CreateItemRequest>("requests")
            .BuildServiceProvider();

        provider.GetRequiredService<IBlobJsonStore<Item>>().Should().BeOfType<BlobJsonStore<Item>>();
        provider.GetRequiredService<IBlobJsonStore<CreateItemRequest>>().Should().BeOfType<BlobJsonStore<CreateItemRequest>>();
        provider.GetRequiredService<BlobJsonStoreOptions<Item>>().Prefix.Should().Be("items");
        provider.GetRequiredService<BlobJsonStoreOptions<CreateItemRequest>>().Prefix.Should().Be("requests");
    }

    [Fact]
    public void AddBlobJsonStore_WithPocAuth_ResolvesFunctionDependenciesWithoutStorageIo()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddBlobJsonStore<Item>("items")
            .AddPocAuth()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        ActivatorUtilities.CreateInstance<App.Api.Functions.ItemsFunction>(provider).Should().NotBeNull();
        ActivatorUtilities.CreateInstance<AuthFunction>(provider).Should().NotBeNull();
        provider.GetRequiredService<IPocAuthStore>().Should().BeOfType<TablePocAuthStore>();
    }
}
