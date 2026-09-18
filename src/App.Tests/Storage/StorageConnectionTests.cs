using App.Api.Storage;
using AwesomeAssertions;

namespace App.Tests.Storage;

/// <summary>
/// Marks <see cref="StorageConnectionTests"/> as a non-parallel xUnit collection so its
/// environment-variable mutations never race another collection reading the same variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StorageConnectionEnvironmentCollection
{
    public const string Name = "StorageConnection environment variables";
}

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class StorageConnectionTests : IDisposable
{
    private const string ConnectionStringVariable = "AZURE_STORAGE_CONNECTION_STRING";
    private const string WebJobsStorageVariable = "AzureWebJobsStorage";

    private readonly string? _originalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
    private readonly string? _originalWebJobsStorage = Environment.GetEnvironmentVariable(WebJobsStorageVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, _originalConnectionString);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, _originalWebJobsStorage);
    }

    [Fact]
    public void ResolveConnectionString_ExplicitConnectionStringSet_TakesPrecedence()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, "explicit-connection-string");
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, "azure-web-jobs-storage-value");

        StorageConnection.ResolveConnectionString().Should().Be("explicit-connection-string");
    }

    [Fact]
    public void ResolveConnectionString_OnlyAzureWebJobsStorageSet_FallsBackToIt()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, "azure-web-jobs-storage-value");

        StorageConnection.ResolveConnectionString().Should().Be("azure-web-jobs-storage-value");
    }

    [Fact]
    public void ResolveConnectionString_NeitherVariableSet_FallsBackToDevelopmentStorage()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, null);

        StorageConnection.ResolveConnectionString().Should().Be("UseDevelopmentStorage=true");
    }
}
