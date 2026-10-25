using System.Net;
using App.Web.Pages;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class HomeTests : TestContext
{
    private void RegisterApiClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) =>
        Services.AddSingleton(DelegateHttpMessageHandler.CreateClient(handle));

    [Fact]
    public void OnInitializedAsync_NetworkFailure_ShowsUnavailable()
    {
        RegisterApiClient((_, _) => throw new HttpRequestException("connection reset"));

        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='api-health']").TextContent.Should().Be("unavailable"));
    }

    [Fact]
    public void OnInitializedAsync_HealthProbe_RequestsOnlyTheHealthEndpoint()
    {
        var requests = new List<string>();
        RegisterApiClient((request, _) =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => requests.Should().Equal("GET /api/health"));
    }

    [Fact]
    public void Home_Renders_RootDataTestId()
    {
        RegisterApiClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var cut = RenderComponent<Home>();

        cut.Find("[data-testid='home-page']").Should().NotBeNull();
    }

    [Fact]
    public void Home_HealthCheckSucceeds_ShowsOk()
    {
        RegisterApiClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='api-health']").TextContent.Should().Be("ok"));
    }

    [Fact]
    public void Home_HealthCheckFails_ShowsUnavailable()
    {
        RegisterApiClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='api-health']").TextContent.Should().Be("unavailable"));
    }

    [Fact]
    public void Home_HealthCheckTimesOut_ShowsUnavailable()
    {
        RegisterApiClient((_, _) => throw new TaskCanceledException("timed out"));

        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='api-health']").TextContent.Should().Be("unavailable"));
    }
}
