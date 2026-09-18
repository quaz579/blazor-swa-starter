using System.Net;
using App.Web.Pages;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class HomeTests : TestContext
{
    private void RegisterApiClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };
        Services.AddSingleton(new ItemsApiClient(httpClient));
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
}
