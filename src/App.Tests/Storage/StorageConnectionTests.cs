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
    private static readonly string[] SettingNames =
    [
        ConnectionStringVariable,
        WebJobsStorageVariable,
        "PREVIEW_STORAGE_CONNECTION_STRING",
        "PRODUCTION_STORAGE_ACCOUNT_NAME",
        "PREVIEW_STORAGE_ACCOUNT_NAME"
    ];

    private readonly Dictionary<string, string?> _originalSettings = SettingNames
        .ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public StorageConnectionTests()
    {
        foreach (var name in SettingNames)
            Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originalSettings)
            Environment.SetEnvironmentVariable(name, value);
    }

    [Fact]
    public void ResolveConnectionString_ExplicitConnectionStringSet_TakesPrecedence()
    {
        var explicitConnection = ConnectionString("explicitaccount");
        Environment.SetEnvironmentVariable(ConnectionStringVariable, explicitConnection);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, ConnectionString("fallbackaccount"));

        StorageConnection.ResolveConnectionString().Should().Be(explicitConnection);
    }

    [Fact]
    public void ResolveConnectionString_OnlyAzureWebJobsStorageSet_FallsBackToIt()
    {
        var connection = ConnectionString("hostaccount");
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, connection);

        StorageConnection.ResolveConnectionString().Should().Be(connection);
    }

    [Fact]
    public void ResolveConnectionString_NeitherVariableSet_FallsBackToDevelopmentStorage()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(WebJobsStorageVariable, null);

        StorageConnection.ResolveConnectionString().Should().Be("UseDevelopmentStorage=true");
    }

    [Fact]
    public void Resolve_MissingMarker_UsesProductionConnectionAndUnverifiedIdentity()
    {
        var connection = ConnectionString("productionaccount");
        var result = StorageConnection.Resolve(NonexistentMarker(), name =>
            name == "AZURE_STORAGE_CONNECTION_STRING" ? connection : null);

        result.Mode.Should().Be("production");
        result.ConnectionString.Should().Be(connection);
        result.Account.Should().Be("productionaccount");
        result.Identity.Should().Be("unverified");
        result.ProductionAccount.Should().BeNull();
    }

    [Fact]
    public void Resolve_PreviewMarker_UsesPreviewConnection()
    {
        var preview = ConnectionString("previewaccount");
        WithMarker("{\"mode\":\"preview\"}", path =>
        {
            var values = new Dictionary<string, string?>
            {
                ["PREVIEW_STORAGE_CONNECTION_STRING"] = preview,
                ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString("productionaccount")
            };

            var result = StorageConnection.Resolve(path, values.GetValueOrDefault);
            result.Mode.Should().Be("preview");
            result.ConnectionString.Should().Be(preview);
            result.Account.Should().Be("previewaccount");
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_PreviewSettingMissing_ThrowsWithoutProductionFallback(string? setting)
    {
        WithMarker("{\"mode\":\"preview\"}", path =>
        {
            var values = new Dictionary<string, string?>
            {
                ["PREVIEW_STORAGE_CONNECTION_STRING"] = setting,
                ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString("productionaccount")
            };
            Action resolve = () => StorageConnection.Resolve(path, values.GetValueOrDefault);
            resolve.Should().Throw<MissingPreviewStorageException>()
                .WithMessage("*PREVIEW_STORAGE_CONNECTION_STRING*production fallback is forbidden*");
        });
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"production\"}")]
    [InlineData("{\"mode\":42}")]
    [InlineData("[]")]
    public void Resolve_InvalidMarker_ThrowsConfigurationException(string marker)
    {
        WithMarker(marker, path =>
        {
            Action resolve = () => StorageConnection.Resolve(path, _ => ConnectionString("productionaccount"));
            resolve.Should().Throw<InvalidStorageModeException>()
                .Which.StorageMode.Should().Be("unknown");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_StorageAccountDoesNotMatchExpectedMode_Throws(bool previewMode)
    {
        var marker = previewMode ? "{\"mode\":\"preview\"}" : null;
        WithOptionalMarker(marker, path =>
        {
            var values = new Dictionary<string, string?>
            {
                ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString(previewMode ? "productionaccount" : "previewaccount"),
                ["PREVIEW_STORAGE_CONNECTION_STRING"] = ConnectionString("productionaccount"),
                ["PRODUCTION_STORAGE_ACCOUNT_NAME"] = "productionaccount",
                ["PREVIEW_STORAGE_ACCOUNT_NAME"] = "previewaccount"
            };

            Action resolve = () => StorageConnection.Resolve(path, values.GetValueOrDefault);
            resolve.Should().Throw<StorageIdentityMismatchException>()
                .Which.StorageMode.Should().Be(previewMode ? "preview" : "production");
        });
    }

    [Theory]
    [InlineData(false, "productionaccount")]
    [InlineData(true, "previewaccount")]
    public void Resolve_ExpectedStorageAccountMatchesMode_ReportsVerifiedIdentity(bool previewMode, string account)
    {
        var marker = previewMode ? "{\"mode\":\"preview\"}" : null;
        WithOptionalMarker(marker, path =>
        {
            var values = new Dictionary<string, string?>
            {
                ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString("productionaccount"),
                ["PREVIEW_STORAGE_CONNECTION_STRING"] = ConnectionString("previewaccount"),
                ["PRODUCTION_STORAGE_ACCOUNT_NAME"] = "productionaccount",
                ["PREVIEW_STORAGE_ACCOUNT_NAME"] = "previewaccount"
            };

            var result = StorageConnection.Resolve(path, values.GetValueOrDefault);
            result.Account.Should().Be(account);
            result.Identity.Should().Be("verified");
            result.ProductionAccount.Should().Be("productionaccount");
        });
    }

    [Fact]
    public void Resolve_PartialExpectedIdentitySettings_ThrowsConfigurationException()
    {
        var values = new Dictionary<string, string?>
        {
            ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString("productionaccount"),
            ["PRODUCTION_STORAGE_ACCOUNT_NAME"] = "productionaccount"
        };

        Action resolve = () => StorageConnection.Resolve(NonexistentMarker(), values.GetValueOrDefault);
        resolve.Should().Throw<StorageIdentityConfigurationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_GarbageConnectionString_ThrowsConfigurationException(bool previewMode)
    {
        var marker = previewMode ? "{\"mode\":\"preview\"}" : null;
        WithOptionalMarker(marker, path =>
        {
            var values = new Dictionary<string, string?>
            {
                ["AZURE_STORAGE_CONNECTION_STRING"] = "garbage connection string",
                ["PREVIEW_STORAGE_CONNECTION_STRING"] = "garbage connection string"
            };
            Action resolve = () => StorageConnection.Resolve(path, values.GetValueOrDefault);
            resolve.Should().Throw<StorageConnectionConfigurationException>()
                .Which.Message.Should().NotContain("garbage connection string");
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

    private static string ConnectionString(string account) =>
        $"DefaultEndpointsProtocol=https;AccountName={account};AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";

    private static string NonexistentMarker() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    private static void WithOptionalMarker(string? marker, Action<string> check)
    {
        if (marker is null)
        {
            check(NonexistentMarker());
            return;
        }

        WithMarker(marker, check);
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
