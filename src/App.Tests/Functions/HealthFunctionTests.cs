using App.Api.Functions;
using App.Tests.PocAuth;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace App.Tests.Functions;

[Collection(App.Tests.Storage.StorageConnectionEnvironmentCollection.Name)]
public sealed class HealthFunctionTests
{
    private readonly TestTimeProvider _time = new();

    [Fact]
    public async Task GetHealth_Always_Returns200WithOkStatusAndUtcTimestamp()
    {
        var request = TestHttpRequestData.CreateRequest();
        var function = new HealthFunction(NullLogger<HealthFunction>.Instance, _time);

        var response = await function.GetHealth(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("ok");

        var utc = body.RootElement.GetProperty("utc").GetDateTimeOffset();
        utc.Should().Be(_time.GetUtcNow());
    }
    [Theory]
    [InlineData(false, "production")]
    [InlineData(true, "preview")]
    public async Task GetHealth_ResolvedStorage_ReportsModeAndAccountWithoutSecrets(bool preview, string expectedMode)
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "storage-mode.json");
        var variable = preview ? "PREVIEW_STORAGE_CONNECTION_STRING" : "AZURE_STORAGE_CONNECTION_STRING";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            if (preview)
                File.WriteAllText(marker, "{\"mode\":\"preview\"}");
            Environment.SetEnvironmentVariable(variable,
                "DefaultEndpointsProtocol=https;AccountName=healthaccount;AccountKey=" + Convert.ToBase64String(new byte[32]) + ";EndpointSuffix=core.windows.net");
            var function = new HealthFunction(NullLogger<HealthFunction>.Instance, _time);
            var response = await function.GetHealth(TestHttpRequestData.CreateRequest(), CancellationToken.None);
            using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
            body.RootElement.GetProperty("storageMode").GetString().Should().Be(expectedMode);
            body.RootElement.GetProperty("storageAccount").GetString().Should().Be("healthaccount");
            body.RootElement.EnumerateObject().Select(property => property.Name).Should()
                .BeEquivalentTo("status", "utc", "storageMode", "storageAccount");
            body.RootElement.GetRawText().Should().NotContain("AccountKey").And.NotContain(Convert.ToBase64String(new byte[32]));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            if (preview)
                File.Delete(marker);
        }
    }
    [Fact]
    public async Task GetHealth_MissingPreviewSetting_ReturnsSafeFailureDiagnostic()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "storage-mode.json");
        var original = Environment.GetEnvironmentVariable("PREVIEW_STORAGE_CONNECTION_STRING");
        try
        {
            File.WriteAllText(marker, "{\"mode\":\"preview\"}");
            Environment.SetEnvironmentVariable("PREVIEW_STORAGE_CONNECTION_STRING", null);
            var request = TestHttpRequestData.CreateRequest();
            var transport = request.CreateResponse();
            transport.When(value => value.StatusCode = Arg.Any<System.Net.HttpStatusCode>()).Do(_ =>
            {
                if (transport.Body.Length > 0)
                    throw new InvalidOperationException("Response has already started");
            });
            var function = new HealthFunction(NullLogger<HealthFunction>.Instance, _time);
            var response = await function.GetHealth(request, CancellationToken.None);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
            using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
            body.RootElement.GetProperty("error").GetString().Should().Contain("PREVIEW_STORAGE_CONNECTION_STRING");
            body.RootElement.EnumerateObject().Select(property => property.Name).Should()
                .BeEquivalentTo("storageMode", "error");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PREVIEW_STORAGE_CONNECTION_STRING", original);
            File.Delete(marker);
        }
    }
}
