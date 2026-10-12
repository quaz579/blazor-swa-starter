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
        return new PocAuthState(http, enabled);
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
    public async Task Initialize_WhenEnabled_ProbesMeWithNoLocalHintAndAdoptsTheServerSession()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"admin\",\"roles\":[\"admin\"]}")), out var requests);

        await state.InitializeAsync();

        requests.Should().Equal("GET /api/auth/me");
        state.User!.Username.Should().Be("admin");
    }

    [Fact]
    public async Task Initialize_WhenMeSaysNotAuthenticated_StaysSignedOut()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"authenticated\":false}")), out _);

        await state.InitializeAsync();

        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_WhenMeReturnsAnErrorStatus_StaysSignedOut()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")), out _);

        await state.InitializeAsync();

        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_WhenMeFailsOrTimesOut_StaysSignedOutWithoutThrowing()
    {
        var failing = Create(true, _ => throw new HttpRequestException("down"), out _);
        var slow = Create(true, _ => throw new TaskCanceledException("timeout"), out _);

        await failing.InitializeAsync();
        await slow.InitializeAsync();

        failing.IsSignedIn.Should().BeFalse();
        slow.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Login_Success_SetsUser()
    {
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

    [Fact]
    public async Task ChangePassword_Success_PostsBothPasswordsAndReturnsNull()
    {
        string? sent = null;
        var state = Create(true, async req =>
        {
            sent = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }, out var requests);

        var error = await state.ChangePasswordAsync("old-pass-1", "new-pass-2");

        error.Should().BeNull();
        requests.Should().Equal("POST /api/auth/change-password");
        sent.Should().Contain("\"currentPassword\":\"old-pass-1\"").And.Contain("\"newPassword\":\"new-pass-2\"");
    }

    [Fact]
    public async Task ChangePassword_BadRequest_ReturnsTheServerMessage()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"error\":\"The current password is incorrect.\"}")), out _);

        (await state.ChangePasswordAsync("a", "b")).Should().Be("The current password is incorrect.");
    }

    [Fact]
    public async Task ChangePassword_BadRequestWithMalformedBody_ReturnsAGenericMessage()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.BadRequest, "<html>nope")), out _);

        (await state.ChangePasswordAsync("a", "b")).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChangePassword_Unauthorized_SignsTheUserOut()
    {
        var calls = 0;
        var state = Create(true, _ => Task.FromResult(calls++ == 0
            ? Json(HttpStatusCode.OK, "{\"username\":\"admin\",\"roles\":[]}")
            : Json(HttpStatusCode.Unauthorized, "{\"error\":\"Sign in required.\"}")), out _);
        await state.LoginAsync("admin", "x");

        var error = await state.ChangePasswordAsync("a", "b");

        error.Should().Contain("Sign in");
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_ServerErrorTransportFailureAndTimeout_ReturnMessagesWithoutThrowing()
    {
        var server = Create(true, _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")), out _);
        var transport = Create(true, _ => throw new HttpRequestException("reset"), out _);
        var timeout = Create(true, _ => throw new TaskCanceledException("timeout"), out _);

        (await server.ChangePasswordAsync("a", "b")).Should().Contain("500");
        (await transport.ChangePasswordAsync("a", "b")).Should().Contain("Could not reach");
        (await timeout.ChangePasswordAsync("a", "b")).Should().Contain("Could not reach");
    }
}
