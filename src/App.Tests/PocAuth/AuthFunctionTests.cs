using System.Net;
using App.Api.PocAuth;
using App.Tests.Functions;
using AwesomeAssertions;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace App.Tests.PocAuth;

[Collection("PocAuthEnv")]
public sealed class AuthFunctionTests : IDisposable
{
    private readonly string? _originalEnabled = Environment.GetEnvironmentVariable(PocAuthSettings.EnabledSetting);
    private readonly InMemoryPocAuthStore _store = new();
    private readonly TestTimeProvider _time = new();
    private readonly AuthFunction _function;
    private readonly PocAuthService _service;

    public AuthFunctionTests()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "true");
        var (salt, hash, iterations) = PocPasswordHasher.Hash("Ch@nageM3", iterations: 1_000);
        _service = new PocAuthService(_store, _time, [new PocSeedUser("requester@example.com", "Requester", ["user"], salt, hash, iterations)]);
        _function = new AuthFunction(_service, _time, NullLogger<AuthFunction>.Instance);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, _originalEnabled);

    private static string SetCookie(HttpResponseData response) => response.Headers.GetValues("Set-Cookie").Single();

    private static string TokenFrom(string setCookie) => setCookie.Split(';')[0]["poc_session=".Length..];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AuthRequest_RequestBodyReadFails_PropagatesTheOperationalFailure(bool changePassword, bool cancelled)
    {
        var login = await _function.Login(TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login",
            new { username = "requester@example.com", password = "Ch@nageM3" }), CancellationToken.None);
        var request = TestHttpRequestData.CreateRequest("POST", changePassword
            ? "http://localhost/api/auth/change-password"
            : "http://localhost/api/auth/login");
        request.Headers.Add("Cookie", SetCookie(login).Split(';')[0]);
        Exception failure = cancelled ? new OperationCanceledException("read canceled") : new IOException("read failed");
        var body = Substitute.For<Stream>();
        body.CanRead.Returns(true);
        body.ReadAsync(Arg.Any<Memory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<int>(failure));
        request.Body.Returns(body);

        var act = () => changePassword
            ? _function.ChangePassword(request, CancellationToken.None)
            : _function.Login(request, CancellationToken.None);

        var thrown = await act.Should().ThrowExactlyAsync<AggregateException>();
        var inner = thrown.Which.InnerExceptions.Should().ContainSingle().Which;
        if (cancelled)
        {
            inner.Should().BeOfType<TaskCanceledException>();
        }
        else
        {
            inner.Should().BeSameAs(failure);
        }
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithUserAndSessionCookie()
    {
        var req = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "Requester@example.com", password = "Ch@nageM3" });

        var response = await _function.Login(req, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("username").GetString().Should().Be("requester@example.com");
        body.RootElement.GetProperty("roles")[0].GetString().Should().Be("user");
        SetCookie(response).Should().StartWith("poc_session=").And.Contain("HttpOnly").And.Contain("SameSite=Lax").And.NotContain("Secure");
    }

    [Fact]
    public async Task Login_OverHttpsForwardedProto_SetsSecure()
    {
        var req = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "requester@example.com", password = "Ch@nageM3" });
        req.Headers.Add("X-Forwarded-Proto", "https");

        var response = await _function.Login(req, CancellationToken.None);

        SetCookie(response).Should().Contain("; Secure");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithoutCookie()
    {
        var req = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "requester@example.com", password = "bad" });

        var response = await _function.Login(req, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Login_MissingOrMalformedBody_Returns400()
    {
        var missing = await _function.Login(TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login"), CancellationToken.None);
        var blank = await _function.Login(TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "", password = "" }), CancellationToken.None);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_WithoutCookie_Returns200AuthenticatedFalse()
    {
        var response = await _function.Me(TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        body.RootElement.TryGetProperty("username", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Me_WithSessionCookie_ReturnsAuthenticatedTrue_AndAfterLogoutAuthenticatedFalse()
    {
        var login = await _function.Login(TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "requester@example.com", password = "Ch@nageM3" }), CancellationToken.None);
        var token = TokenFrom(SetCookie(login));

        var meReq = TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me");
        meReq.Headers.Add("Cookie", $"other=1; poc_session={token}");
        var me = await _function.Me(meReq, CancellationToken.None);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var meBody = await TestHttpRequestData.ReadBodyAsJsonAsync(me))
        {
            meBody.RootElement.GetProperty("authenticated").GetBoolean().Should().BeTrue();
            meBody.RootElement.GetProperty("username").GetString().Should().Be("requester@example.com");
            meBody.RootElement.GetProperty("roles")[0].GetString().Should().Be("user");
        }

        var logoutReq = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/logout");
        logoutReq.Headers.Add("Cookie", $"poc_session={token}");
        var logout = await _function.Logout(logoutReq, CancellationToken.None);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookie(logout).Should().Contain("Max-Age=0");

        var again = TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me");
        again.Headers.Add("Cookie", $"poc_session={token}");
        var afterLogout = await _function.Me(again, CancellationToken.None);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.OK);
        using var afterBody = await TestHttpRequestData.ReadBodyAsJsonAsync(afterLogout);
        afterBody.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AllEndpoints_WhenAuthDisabled_Return404()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "false");

        (await _function.Login(TestHttpRequestData.CreateRequest("POST"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _function.Logout(TestHttpRequestData.CreateRequest("POST"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _function.Me(TestHttpRequestData.CreateRequest(), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _function.ChangePassword(TestHttpRequestData.CreateRequest("POST"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<string> SignInAsync()
    {
        var login = await _function.Login(
            TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "requester@example.com", password = "Ch@nageM3" }),
            CancellationToken.None);
        return TokenFrom(SetCookie(login));
    }

    private Task<HttpResponseData> ChangeAsync(string? token, object? body)
    {
        var req = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/change-password", body);
        if (token is not null)
        {
            req.Headers.Add("Cookie", $"poc_session={token}");
        }

        return _function.ChangePassword(req, CancellationToken.None);
    }

    [Fact]
    public async Task ChangePassword_Anonymous_Returns401AndChangesNothing()
    {
        var before = _store.Users.Count;

        var response = await ChangeAsync(null, new { currentPassword = "Ch@nageM3", newPassword = "Brand-new-pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _store.Users.Count.Should().Be(before);
        (await _service.AuthenticateAsync("requester@example.com", "Ch@nageM3")).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_ExpiredOrUnknownSession_Returns401()
    {
        (await ChangeAsync("not-a-real-token", new { currentPassword = "Ch@nageM3", newPassword = "Brand-new-pass1" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_ValidRequest_Returns204AndOnlyTheNewPasswordWorks()
    {
        var token = await SignInAsync();

        var response = await ChangeAsync(token, new { currentPassword = "Ch@nageM3", newPassword = "Brand-new-pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _service.AuthenticateAsync("requester@example.com", "Brand-new-pass1")).Should().NotBeNull();
        (await _service.AuthenticateAsync("requester@example.com", "Ch@nageM3")).Should().BeNull();
        (await _service.ValidateSessionAsync(token)).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_WhenOtherSessionsCannotBeRevoked_Returns200WithAWarningBody()
    {
        var token = await SignInAsync();
        _store.ThrowOnSessionDelete = true;

        var response = await ChangeAsync(token, new { currentPassword = "Ch@nageM3", newPassword = "Brand-new-pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("warning").GetString().Should().Be("Password changed, but other devices could not be signed out.");
        (await _service.AuthenticateAsync("requester@example.com", "Brand-new-pass1")).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400AndKeepsTheOldPassword()
    {
        var token = await SignInAsync();

        var response = await ChangeAsync(token, new { currentPassword = "wrong", newPassword = "Brand-new-pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Contain("current password");
        (await _service.AuthenticateAsync("requester@example.com", "Ch@nageM3")).Should().NotBeNull();
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("alllettersnodigit")]
    [InlineData("12345678901")]
    [InlineData("Ch@nageM3")]
    public async Task ChangePassword_WeakNewPassword_Returns400AndKeepsTheOldPassword(string candidate)
    {
        var token = await SignInAsync();

        var response = await ChangeAsync(token, new { currentPassword = "Ch@nageM3", newPassword = candidate });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _service.AuthenticateAsync("requester@example.com", "Ch@nageM3")).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_MissingOrMalformedBody_Returns400()
    {
        var token = await SignInAsync();

        var missing = await ChangeAsync(token, null);
        var blank = await ChangeAsync(token, new { currentPassword = "", newPassword = "" });
        var malformedReq = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/change-password");
        malformedReq.Body.Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{not json")));
        malformedReq.Headers.Add("Cookie", $"poc_session={token}");
        var malformed = await _function.ChangePassword(malformedReq, CancellationToken.None);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

[CollectionDefinition("PocAuthEnv", DisableParallelization = true)]
public sealed class PocAuthEnvCollection;
