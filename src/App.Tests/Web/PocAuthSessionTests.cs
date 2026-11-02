using System.Net;
using System.Text;
using App.Web.Services;
using App.Web.Layout;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class PocAuthSessionTests : TestContext
{
    private PocAuthState Create(bool enabled, Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, out List<string> requests)
    {
        var http = DelegateHttpMessageHandler.CreateRecordingClient(handle, out requests);
        return new PocAuthState(http, enabled, timeProvider: new ProbeTimeProvider(), retryBackoff: TimeSpan.Zero);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task LogoutAsync_ServerRejectsLogout_NavigationShowsTheErrorAndRetry()
    {
        var attempts = 0;
        var state = Create(true, request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("logout")
            ? new HttpResponseMessage(attempts++ == 0 ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent)
            : Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"admin\",\"roles\":[\"admin\"]}")), out _);
        await state.InitializeAsync();
        Services.AddSingleton(state);
        var cut = RenderComponent<NavMenu>();

        await cut.Find("[data-testid='nav-logout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='nav-logout-error']").TextContent.Should().Be("Sign-out failed with status 500. Try again.");
        cut.Find("[data-testid='nav-user']").TextContent.Should().Be("admin");
        cut.Find("[data-testid='nav-logout']").HasAttribute("disabled").Should().BeFalse();

        await cut.Find("[data-testid='nav-logout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='nav-login']").Should().NotBeNull();
        cut.FindAll("[data-testid='nav-user']").Should().BeEmpty();
        cut.FindAll("[data-testid='nav-logout-error']").Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_PendingProbe_MarksNavigationBusyUntilCompletion()
    {
        var probe = new TaskCompletionSource<HttpResponseMessage>();
        var state = Create(true, _ => probe.Task, out var requests);
        Services.AddSingleton(state);
        var cut = RenderComponent<NavMenu>();
        var initialization = state.InitializeAsync();
        cut.Find("[data-testid=nav-links]").GetAttribute("aria-busy").Should().Be("true");

        await RenderCompletion.AfterAsync(cut,
            () => cut.Find("[data-testid=nav-links]").GetAttribute("aria-busy") == "false", async () =>
            {
                probe.SetResult(Json(HttpStatusCode.OK, "{\"authenticated\":false}"));
                await initialization;
            });

        cut.Find("[data-testid=nav-links]").GetAttribute("aria-busy").Should().Be("false");
        requests.Should().Equal("GET /api/auth/me");
        state.ProbeFailed.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_ProbeCompletesAfterLogin_PreservesTheNewIdentity(bool authenticated)
    {
        var probe = new TaskCompletionSource<HttpResponseMessage>();
        var state = Create(true, request => request.Method == HttpMethod.Get
            ? probe.Task
            : Task.FromResult(Json(HttpStatusCode.OK, "{\"username\":\"new-user\",\"roles\":[\"user\"]}")), out _);
        var initialization = state.InitializeAsync();
        await state.LoginAsync("new-user", "fixture-pass1");
        var notifications = 0;
        state.Changed += () => notifications++;

        probe.SetResult(Json(HttpStatusCode.OK, authenticated
            ? "{\"authenticated\":true,\"username\":\"old-user\",\"roles\":[\"admin\"]}"
            : "{\"authenticated\":false}"));
        await initialization;

        state.User.Should().BeEquivalentTo(new PocUser("new-user", ["user"]));
        notifications.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_ProbeCompletesAfterLogout_StaysSignedOut()
    {
        var probe = new TaskCompletionSource<HttpResponseMessage>();
        var state = Create(true, request => request.Method == HttpMethod.Get
            ? probe.Task
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), out _);
        var initialization = state.InitializeAsync();
        await state.LogoutAsync();

        probe.SetResult(Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"old-user\",\"roles\":[\"admin\"]}"));
        await initialization;

        state.User.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_ProbeFailsAfterAuthCommand_PreservesIdentityWithoutRetry(bool signedIn)
    {
        var probe = new TaskCompletionSource<HttpResponseMessage>();
        var state = Create(true, request => request.Method == HttpMethod.Get
            ? probe.Task
            : Task.FromResult(signedIn
                ? Json(HttpStatusCode.OK, "{\"username\":\"new-user\",\"roles\":[\"user\"]}")
                : new HttpResponseMessage(HttpStatusCode.NoContent)), out var requests);
        var initialization = state.InitializeAsync();
        if (signedIn)
        {
            (await state.LoginAsync("new-user", "fixture-pass1")).Should().BeNull();
        }
        else
        {
            (await state.LogoutAsync()).Should().BeNull();
        }
        state.InitializeAsync().Should().BeSameAs(initialization);
        var notifications = 0;
        state.Changed += () => notifications++;

        probe.SetException(new HttpRequestException("down"));
        await initialization;

        state.IsSignedIn.Should().Be(signedIn);
        state.ProbeFailed.Should().BeFalse();
        notifications.Should().Be(0);
        requests.Should().Equal("GET /api/auth/me", signedIn ? "POST /api/auth/login" : "POST /api/auth/logout");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginAsync_TransportFailure_ReturnsAnError(bool timeout)
    {
        var state = Create(true, _ => timeout
            ? throw new TaskCanceledException("timeout")
            : throw new HttpRequestException("connection reset"), out _);

        var error = await state.LoginAsync("admin", "fixture-pass1");

        error.Should().Be("Could not reach the API. Try again.");
        state.User.Should().BeNull();
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("change-password")]
    public async Task AuthOperationAsync_CallerCancels_PropagatesCancellation(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var state = Create(true, _ => throw new OperationCanceledException(cancellation.Token), out _);

        Func<Task> act = operation switch
        {
            "login" => () => state.LoginAsync("admin", "fixture-pass1", cancellation.Token),
            "logout" => () => state.LogoutAsync(cancellation.Token),
            _ => () => state.ChangePasswordAsync("old-pass1", "new-pass2", cancellation.Token),
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LogoutAsync_ServerRejectsLogout_PreservesIdentityAndAllowsRetry()
    {
        var attempts = 0;
        var state = Create(true, request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(HttpStatusCode.OK, "{\"username\":\"admin\",\"roles\":[\"admin\"]}")
            : new HttpResponseMessage(attempts++ == 0 ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent)), out _);
        await state.LoginAsync("admin", "fixture-pass1");

        var error = await state.LogoutAsync();

        error.Should().Be("Sign-out failed with status 500. Try again.");
        state.User!.Username.Should().Be("admin");
        (await state.LogoutAsync()).Should().BeNull();
        state.User.Should().BeNull();
    }

}
