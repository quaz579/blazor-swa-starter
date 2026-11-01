using System.Net;
using System.Text;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;

namespace App.Tests.Web;

public sealed class PocAuthStateTests : TestContext
{
    private PocAuthState Create(bool enabled, Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, out List<string> requests, TimeSpan? probeTimeout = null)
    {
        var http = DelegateHttpMessageHandler.CreateRecordingClient(handle, out requests);
        http.Timeout = Timeout.InfiniteTimeSpan;
        return new PocAuthState(http, enabled, probeTimeout, timeProvider: new ProbeTimeProvider(), retryBackoff: TimeSpan.Zero);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"authenticated\":null}")]
    [InlineData("{\"authenticated\":true,\"username\":\"admin\",\"roles\":[null]}")]
    [InlineData("{\"authenticated\":true,\"username\":\"admin\",\"roles\":[\"admin\",null]}")]
    [InlineData("{\"authenticated\":true,\"username\":\"admin\",\"roles\":[\" \"]}")]
    [InlineData("{\"authenticated\":true,\"username\":\"admin\",\"roles\":null}")]
    [InlineData("{\"authenticated\":true,\"username\":\"admin\"}")]
    [InlineData("{\"authenticated\":true,\"username\":\"\",\"roles\":[]}")]
    [InlineData("{\"authenticated\":true,\"username\":\" \",\"roles\":[]}")]
    public async Task InitializeAsync_IncompleteIdentity_DoesNotPublishAUser(string body)
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, body)), out _);

        await state.InitializeAsync();

        state.User.Should().BeNull();
        state.ProbeFailed.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_UnexpectedHandlerFailure_PropagatesAndAllowsLaterRetry()
    {
        var calls = 0;
        var state = Create(true, _ => ++calls == 1
            ? throw new InvalidOperationException("handler failed")
            : Task.FromResult(Json(HttpStatusCode.OK, "{\"authenticated\":false}")), out _);

        Func<Task> initialize = () => state.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        await state.InitializeAsync();

        calls.Should().Be(2);
        state.ProbeFailed.Should().BeFalse();
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_AwaitsTheSameSessionProbe()
    {
        var session = Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"admin\",\"roles\":[]}");
        var response = new TaskCompletionSource<HttpResponseMessage>();
        var state = Create(true, _ => response.Task, out var requests, Timeout.InfiniteTimeSpan);
        var notifications = 0;
        state.Changed += () => notifications++;

        var first = state.InitializeAsync();
        var second = state.InitializeAsync();
        var secondWasCompleted = second.IsCompleted;
        var pendingRequests = requests.ToArray();
        response.SetResult(session);
        await Task.WhenAll(first, second);

        secondWasCompleted.Should().BeFalse();
        second.Should().BeSameAs(first);
        pendingRequests.Should().Equal("GET /api/auth/me");
        requests.Should().Equal("GET /api/auth/me");
        state.User!.Username.Should().Be("admin");
        notifications.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_TimeoutThenRetry_PublishesSuccessOrFailureAndAllowsRecovery(bool succeeds)
    {
        var clock = new ProbeTimeProvider();
        var calls = 0;
        using var http = DelegateHttpMessageHandler.CreateClient((_, token) =>
        {
            calls++;
            if (calls > 2 || (calls == 2 && succeeds))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"admin\",\"roles\":[]}"));
            }
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        });
        var state = new PocAuthState(http, true, timeProvider: clock, retryBackoff: TimeSpan.FromSeconds(3));

        var initialization = state.InitializeAsync();
        (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(25));
        (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(3));
        if (!succeeds)
        {
            (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(25));
        }
        await initialization;

        calls.Should().Be(2);
        state.ProbeFailed.Should().Be(!succeeds);
        state.IsSignedIn.Should().Be(succeeds);
        if (!succeeds)
        {
            state.User.Should().BeNull();
        }
        await state.InitializeAsync();
        calls.Should().Be(succeeds ? 2 : 3);
        state.ProbeFailed.Should().BeFalse();
        state.User!.Username.Should().Be("admin");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("change-password")]
    public async Task AuthOperationAsync_InternalDeadline_ReturnsTransportErrorWithoutWaiting(string operation)
    {
        var clock = new ProbeTimeProvider();
        using var http = DelegateHttpMessageHandler.CreateClient((_, token) =>
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        });
        var state = new PocAuthState(http, true, timeProvider: clock);
        var command = operation switch
        {
            "login" => state.LoginAsync("admin", "fixture-pass1"),
            "logout" => state.LogoutAsync(),
            _ => PasswordErrorAsync(),
        };
        async Task<string?> PasswordErrorAsync() => (await state.ChangePasswordAsync("old-pass1", "new-pass2")).Error;

        (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(30));

        (await command).Should().Be("Could not reach the API. Try again.");
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCallsDuringBackoff_ShareOneBoundedRetry()
    {
        var clock = new ProbeTimeProvider();
        var attempts = 0;
        using var http = DelegateHttpMessageHandler.CreateClient((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("down");
        });
        var state = new PocAuthState(http, true, timeProvider: clock);
        var first = state.InitializeAsync();
        var second = state.InitializeAsync();
        second.Should().BeSameAs(first);
        attempts.Should().Be(1);

        (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(1));
        await Task.WhenAll(first, second);

        attempts.Should().Be(2);
        state.ProbeFailed.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_CallerCancelsPendingProbe_LeavesUserNullWithoutThrowing()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = DelegateHttpMessageHandler.CreateClient(async (_, token) =>
        {
            started.SetResult();
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            using var registration = token.Register(() => pending.TrySetCanceled(token));
            return await pending.Task;
        });
        http.Timeout = Timeout.InfiniteTimeSpan;
        var state = new PocAuthState(http, true, Timeout.InfiniteTimeSpan, timeProvider: new ProbeTimeProvider());
        var initialization = state.InitializeAsync(cancellation.Token);
        await started.Task;

        initialization.IsCompleted.Should().BeFalse();
        cancellation.Cancel();
        await initialization;

        state.User.Should().BeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"username\":null,\"roles\":[]}")]
    [InlineData("{\"username\":\" \",\"roles\":[]}")]
    [InlineData("{\"username\":\"admin\"}")]
    [InlineData("{\"username\":\"admin\",\"roles\":null}")]
    [InlineData("{\"username\":\"admin\",\"roles\":[null]}")]
    [InlineData("{\"username\":\"admin\",\"roles\":[\"admin\",null]}")]
    [InlineData("{\"username\":\"admin\",\"roles\":[\" \"]}")]
    [InlineData("{\"username\":\"admin\",\"roles\":[\"admin\",\"\"]}")]
    public async Task LoginAsync_IncompleteIdentity_ReturnsErrorWithoutSigningIn(string body)
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, body)), out _);
        var notifications = 0;
        state.Changed += () => notifications++;

        var error = await state.LoginAsync("admin", "fixture-pass1");

        error.Should().Be("Sign-in failed: the API returned an unexpected response.");
        state.User.Should().BeNull();
        notifications.Should().Be(0);
    }

    [Fact]
    public async Task Initialize_WhenDisabled_NeverCallsTheApi()
    {
        var state = Create(false, _ => Task.FromResult(Json(HttpStatusCode.OK, "{}")), out var requests);

        await state.InitializeAsync();

        requests.Should().BeEmpty();
        state.ProbeFailed.Should().BeFalse();
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

        state.ProbeFailed.Should().BeFalse();
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ServerError_ExposesProbeFailure()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")), out _);

        await state.InitializeAsync();

        state.ProbeFailed.Should().BeTrue();
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_TransportFailure_ExposesProbeFailure()
    {
        var failing = Create(true, _ => throw new HttpRequestException("down"), out _);
        var slow = Create(true, _ => throw new TaskCanceledException("timeout"), out _);

        await failing.InitializeAsync();
        await slow.InitializeAsync();

        failing.ProbeFailed.Should().BeTrue();
        slow.ProbeFailed.Should().BeTrue();
        failing.IsSignedIn.Should().BeFalse();
        slow.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_PublishesSignedOutWithoutFailure()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")), out var requests);
        await state.InitializeAsync();
        await state.InitializeAsync();
        requests.Should().Equal("GET /api/auth/me");
        state.IsSignedIn.Should().BeFalse();
        state.ProbeFailed.Should().BeFalse();
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
    public async Task LogoutAsync_RequestTimesOut_PreservesTheSignedInIdentity()
    {
        var state = Create(true, request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Task.FromResult(Json(HttpStatusCode.OK, "{\"username\":\"admin\",\"roles\":[\"admin\"]}"))
            : throw new TaskCanceledException("timeout"), out _);
        await state.LoginAsync("admin", "fixture-pass1");

        var error = await state.LogoutAsync();

        error.Should().Be("Could not reach the API. Try again.");
        state.User!.Username.Should().Be("admin");
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

        var result = await state.ChangePasswordAsync("old-pass-1", "new-pass-2");

        result.Should().Be(new PasswordChangeResult(null, null));
        requests.Should().Equal("POST /api/auth/change-password");
        sent.Should().Contain("\"currentPassword\":\"old-pass-1\"").And.Contain("\"newPassword\":\"new-pass-2\"");
    }

    [Fact]
    public async Task ChangePassword_BadRequest_ReturnsTheServerMessage()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"error\":\"The current password is incorrect.\"}")), out _);

        (await state.ChangePasswordAsync("a", "b")).Error.Should().Be("The current password is incorrect.");
    }

    [Fact]
    public async Task ChangePassword_BadRequestWithMalformedBody_ReturnsAGenericMessage()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.BadRequest, "<html>nope")), out _);

        (await state.ChangePasswordAsync("a", "b")).Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChangePassword_Unauthorized_SignsTheUserOut()
    {
        var calls = 0;
        var state = Create(true, _ => Task.FromResult(calls++ == 0
            ? Json(HttpStatusCode.OK, "{\"username\":\"admin\",\"roles\":[]}")
            : Json(HttpStatusCode.Unauthorized, "{\"error\":\"Sign in required.\"}")), out _);
        await state.LoginAsync("admin", "x");

        var result = await state.ChangePasswordAsync("a", "b");

        result.Error.Should().Contain("Sign in");
        state.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_ServerErrorTransportFailureAndTimeout_ReturnMessagesWithoutThrowing()
    {
        var server = Create(true, _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")), out _);
        var transport = Create(true, _ => throw new HttpRequestException("reset"), out _);
        var timeout = Create(true, _ => throw new TaskCanceledException("timeout"), out _);

        (await server.ChangePasswordAsync("a", "b")).Error.Should().Contain("500");
        (await transport.ChangePasswordAsync("a", "b")).Error.Should().Contain("Could not reach");
        (await timeout.ChangePasswordAsync("a", "b")).Error.Should().Contain("Could not reach");
    }

    [Fact]
    public async Task ChangePassword_OkWithWarning_SucceedsAndSurfacesTheWarning()
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"warning\":\"Password changed, but other devices could not be signed out.\"}")), out _);

        var result = await state.ChangePasswordAsync("a", "b");

        result.Error.Should().BeNull();
        result.Warning.Should().Be("Password changed, but other devices could not be signed out.");
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"warning\": null}")]
    [InlineData("{\"warning\": \" \"}")]
    public async Task ChangePasswordAsync_UnreadableOrMissingWarning_ReturnsUnconfirmedRevocationWarning(string body)
    {
        var state = Create(true, _ => Task.FromResult(Json(HttpStatusCode.OK, body)), out _);

        var result = await state.ChangePasswordAsync("old-pass-1", "new-pass-2");

        result.Error.Should().BeNull();
        result.Warning.Should().Be("Password changed, but sign-out of other devices could not be confirmed.");
    }

    [Fact]
    public async Task ChangePassword_Conflict_ReturnsTheServerMessageOrAConflictSpecificFallback()
    {
        var withBody = Create(true, _ => Task.FromResult(Json(HttpStatusCode.Conflict, "{\"error\":\"The password was changed by another request. Enter the new current password and try again.\"}")), out _);
        var withoutBody = Create(true, _ => Task.FromResult(Json(HttpStatusCode.Conflict, "<html>")), out _);

        (await withBody.ChangePasswordAsync("a", "b")).Error.Should().Contain("Enter the new current password");
        (await withoutBody.ChangePasswordAsync("a", "b")).Error.Should().Contain("changed by another request").And.Contain("Enter the new current password");
    }
}
