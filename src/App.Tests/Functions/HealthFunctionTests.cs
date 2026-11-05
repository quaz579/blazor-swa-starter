using App.Api.Functions;
using App.Api.Storage;
using App.Tests.PocAuth;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Functions;

[Collection(App.Tests.Storage.StorageConnectionEnvironmentCollection.Name)]
public sealed class HealthFunctionTests
{
    private readonly TestTimeProvider _time = new();

    [Fact]
    public async Task GetHealth_DevelopmentStorage_ReportsUnverifiedIdentityAndUtcTimestamp()
    {
        var response = await RunHealthAsync(null, new Dictionary<string, string?>
        {
            ["AZURE_STORAGE_CONNECTION_STRING"] = "UseDevelopmentStorage=true"
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("ok");
        body.RootElement.GetProperty("utc").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        body.RootElement.GetProperty("storageMode").GetString().Should().Be("production");
        body.RootElement.GetProperty("storageAccount").GetString().Should().Be("devstoreaccount1");
        body.RootElement.GetProperty("storageIdentity").GetString().Should().Be("unverified");
        body.RootElement.GetProperty("productionStorageAccount").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false, "production", "prodaccount")]
    [InlineData(true, "preview", "previewaccount")]
    public async Task GetHealth_VerifiedStorage_ReportsModeAndAccountWithoutSecrets(bool preview, string expectedMode, string account)
    {
        var connection = ConnectionString(account);
        var settings = new Dictionary<string, string?>
        {
            [preview ? "PREVIEW_STORAGE_CONNECTION_STRING" : "AZURE_STORAGE_CONNECTION_STRING"] = connection,
            ["PRODUCTION_STORAGE_ACCOUNT_NAME"] = "prodaccount",
            ["PREVIEW_STORAGE_ACCOUNT_NAME"] = "previewaccount"
        };
        var response = await RunHealthAsync(preview ? "{\"mode\":\"preview\"}" : null, settings);

        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("storageMode").GetString().Should().Be(expectedMode);
        body.RootElement.GetProperty("storageAccount").GetString().Should().Be(account);
        body.RootElement.GetProperty("storageIdentity").GetString().Should().Be("verified");
        body.RootElement.GetProperty("productionStorageAccount").GetString().Should().Be("prodaccount");
        body.RootElement.GetRawText().Should().NotContain("AccountKey").And.NotContain(Convert.ToBase64String(new byte[32]));
    }

    [Fact]
    public async Task GetHealth_MissingPreviewSetting_ThrowsConfigurationExceptionForMiddleware()
    {
        var settings = new Dictionary<string, string?>
        {
            ["AZURE_STORAGE_CONNECTION_STRING"] = ConnectionString("prodaccount"),
            ["PRODUCTION_STORAGE_ACCOUNT_NAME"] = "prodaccount",
            ["PREVIEW_STORAGE_ACCOUNT_NAME"] = "previewaccount"
        };

        var act = async () => await RunHealthAsync("{\"mode\":\"preview\"}", settings);

        await act.Should().ThrowAsync<MissingPreviewStorageException>()
            .WithMessage("*PREVIEW_STORAGE_CONNECTION_STRING*production fallback is forbidden*");
    }

    private async Task<Microsoft.Azure.Functions.Worker.Http.HttpResponseData> RunHealthAsync(
        string? marker,
        Dictionary<string, string?> settings)
    {
        var settingNames = new[]
        {
            "AZURE_STORAGE_CONNECTION_STRING",
            "AzureWebJobsStorage",
            "PREVIEW_STORAGE_CONNECTION_STRING",
            "PRODUCTION_STORAGE_ACCOUNT_NAME",
            "PREVIEW_STORAGE_ACCOUNT_NAME"
        };
        var originalSettings = settingNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var markerPath = Path.Combine(AppContext.BaseDirectory, "storage-mode.json");
        var originalMarker = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
        try
        {
            foreach (var name in settingNames)
                Environment.SetEnvironmentVariable(name, settings.GetValueOrDefault(name));

            if (marker is null)
                File.Delete(markerPath);
            else
                File.WriteAllText(markerPath, marker);

            var function = new HealthFunction(NullLogger<HealthFunction>.Instance, _time);
            return await function.GetHealth(TestHttpRequestData.CreateRequest(), CancellationToken.None);
        }
        finally
        {
            foreach (var (name, value) in originalSettings)
                Environment.SetEnvironmentVariable(name, value);

            if (originalMarker is null)
                File.Delete(markerPath);
            else
                File.WriteAllText(markerPath, originalMarker);
        }
    }

    private static string ConnectionString(string account) =>
        $"DefaultEndpointsProtocol=https;AccountName={account};AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";
}
