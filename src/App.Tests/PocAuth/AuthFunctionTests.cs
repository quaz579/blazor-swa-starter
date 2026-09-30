using System.Net;
using App.Api.PocAuth;
using App.Tests.Functions;
using AwesomeAssertions;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task Me_WithoutCookie_Returns401()
    {
        var response = await _function.Me(TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithSessionCookie_Returns200_AndAfterLogoutReturns401WithExpiredCookie()
    {
        var login = await _function.Login(TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/login", new { username = "requester@example.com", password = "Ch@nageM3" }), CancellationToken.None);
        var token = TokenFrom(SetCookie(login));

        var meReq = TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me");
        meReq.Headers.Add("Cookie", $"other=1; poc_session={token}");
        var me = await _function.Me(meReq, CancellationToken.None);
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        var logoutReq = TestHttpRequestData.CreateRequest("POST", "http://localhost/api/auth/logout");
        logoutReq.Headers.Add("Cookie", $"poc_session={token}");
        var logout = await _function.Logout(logoutReq, CancellationToken.None);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookie(logout).Should().Contain("Max-Age=0");

        var again = TestHttpRequestData.CreateRequest(url: "http://localhost/api/auth/me");
        again.Headers.Add("Cookie", $"poc_session={token}");
        (await _function.Me(again, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllEndpoints_WhenAuthDisabled_Return404()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "false");

        (await _function.Login(TestHttpRequestData.CreateRequest("POST"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _function.Logout(TestHttpRequestData.CreateRequest("POST"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _function.Me(TestHttpRequestData.CreateRequest(), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

[CollectionDefinition("PocAuthEnv", DisableParallelization = true)]
public sealed class PocAuthEnvCollection;
