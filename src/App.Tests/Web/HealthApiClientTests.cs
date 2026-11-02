using System.Net;
using App.Web.Services;
using AwesomeAssertions;

namespace App.Tests.Web;

public sealed class HealthApiClientTests
{
    private static HealthApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };
        return new HealthApiClient(httpClient);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthEndpointReturns200_ReturnsTrue()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await client.CheckHealthAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHealthAsync_HealthEndpointReturns500_ReturnsFalse()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.CheckHealthAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckHealthAsync_NetworkFailure_ReturnsFalse()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("boom"));

        var result = await client.CheckHealthAsync();

        result.Should().BeFalse();
    }
}
