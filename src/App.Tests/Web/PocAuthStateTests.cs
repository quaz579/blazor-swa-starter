using System.Net;
using System.Text;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;

namespace App.Tests.Web;

public sealed class PocAuthStateTests : TestContext
{
    private PocAuthState Create(bool enabled, Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, out List<string> requests)
    {
        var seen = new List<string>();
        requests = seen;
        var http = new HttpClient(new DelegateHttpMessageHandler((req, _) =>
        {
            seen.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
            return handle(req);
        })) { BaseAddress = new Uri("https://api.test/") };
        return new PocAuthState(http, JSInterop.JSRuntime, enabled);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Initialize_WhenDisabled_NeverCallsTheApi()
    {
        var state = Create(false, _ => Task.FromResult(Json(HttpStatusCode.OK, "{}")), out var requests);

        await state.InitializeAsync();

        requests.Should().BeEmpty();
        state.Enabled.Should().BeFalse();
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_WhenEnabledWithoutSignedInHint_DoesNotCallMe()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "poc_signed_in").SetResult(null);
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{}")), out var requests);

        await state.InitializeAsync();

        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_Success_SetsUser()
    {
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"username\":\"admin\",\"roles\":[\"admin\"]}")), out _);

        var error = await state.LoginAsync("admin", "Ch@nageM3");

        error.Should().BeNull();
        state.User!.Username.Should().Be("admin");
        state.User.Roles.Should().Equal("admin");
    }

    [Fact]
    public async Task Login_BadCredentials_ReturnsMessageAndStaysSignedOut()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"error\":\"no\"}")), out _);

        var error = await state.LoginAsync("admin", "bad");

        error.Should().Be("Invalid username or password.");
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_WhenTheRequestTimesOut_StillSignsOutLocally()
    {
        JSInterop.SetupVoid("localStorage.removeItem", _ => true).SetVoidResult();
        var state = Create(true, _ => throw new TaskCanceledException("timeout"), out _);

        await state.LogoutAsync();

        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Login_MalformedBody_ReturnsMessageInsteadOfThrowing()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{not json")), out _);

        var error = await state.LoginAsync("admin", "Ch@nageM3");

        error.Should().NotBeNull();
        state.IsSignedIn.Should().BeFalse();
    }
}
