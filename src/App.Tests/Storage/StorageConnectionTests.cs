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
    [Fact]
    public void Resolve_MissingMarker_UsesProductionConnection()
    {
        var result = StorageConnection.Resolve(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            name => name == "AZURE_STORAGE_CONNECTION_STRING" ? "production" : "preview");
        result.Should().Be(("production", "production"));
    }

    [Fact]
    public void Resolve_PreviewMarker_UsesPreviewConnection()
    {
        WithMarker("{\"mode\":\"preview\"}", path =>
            StorageConnection.Resolve(path, name => name == "PREVIEW_STORAGE_CONNECTION_STRING" ? "preview" : "production")
                .Should().Be(("preview", "preview")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_PreviewSettingMissing_ThrowsWithoutProductionFallback(string? setting)
    {
        WithMarker("{\"mode\":\"preview\"}", path =>
        {
            Action resolve = () => StorageConnection.Resolve(path,
                name => name == "PREVIEW_STORAGE_CONNECTION_STRING" ? setting : "production");
            resolve.Should().Throw<InvalidOperationException>().WithMessage("*PREVIEW_STORAGE_CONNECTION_STRING*production fallback is forbidden*");
        });
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"production\"}")]
    [InlineData("{\"mode\":42}")]
    [InlineData("[]")]
    public void Resolve_InvalidMarker_Throws(string marker)
    {
        WithMarker(marker, path =>
        {
            Action resolve = () => StorageConnection.Resolve(path, _ => "production");
            resolve.Should().Throw<InvalidOperationException>().WithMessage("Invalid storage-mode.json*");
        });
    }

    [Fact]
    public void StorageConsumers_ConnectionResolution_UsesSharedResolver()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BlazorSwaStarter.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var api = Path.Combine(root.FullName, "src", "App.Api");
        foreach (var file in Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            if (Path.GetFileName(file) != "StorageConnection.cs")
                File.ReadAllText(file).Should().NotContain("GetEnvironmentVariable(\"AZURE_STORAGE_CONNECTION_STRING\")")
                    .And.NotContain("GetEnvironmentVariable(\"PREVIEW_STORAGE_CONNECTION_STRING\")")
                    .And.NotContain("GetEnvironmentVariable(\"AzureWebJobsStorage\")");
        }
        foreach (var file in new[] { "Storage/ServiceCollectionExtensions.cs", "Storage/StorageClients.cs", "PocAuth/ServiceCollectionExtensions.cs" })
            File.ReadAllText(Path.Combine(api, file)).Should().Contain("StorageConnection.ResolveConnectionString()");
        var auth = File.ReadAllText(Path.Combine(api, "PocAuth/TablePocAuthStore.cs"));
        auth.Should().Contain("StorageClients.CreateTableClient(connectionString, PocAuthSettings.UsersTable)")
            .And.Contain("StorageClients.CreateTableClient(connectionString, PocAuthSettings.SessionsTable)");
    }

    private static void WithMarker(string marker, Action<string> check)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, marker);
            check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
