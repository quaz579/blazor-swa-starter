using App.Api.PocAuth;
using AwesomeAssertions;

namespace App.Tests.PocAuth;

public sealed class PocAuthServiceTests
{
    private readonly InMemoryPocAuthStore _store = new();
    private readonly TestTimeProvider _time = new();

    private static PocSeedUser SeedUser(string username, string password, params string[] roles)
    {
        var (salt, hash, iterations) = PocPasswordHasher.Hash(password, iterations: 1_000);
        return new PocSeedUser(username, username, roles, salt, hash, iterations);
    }

    private PocAuthService CreateService(params PocSeedUser[] seed) => new(_store, _time, seed);

    [Fact]
    public async Task Seed_RunsIdempotently_AndNeverOverwritesExistingUsers()
    {
        var service = CreateService(SeedUser("Requester@Example.com", "Ch@nageM3", "user"));

        await service.SeedAsync();
        var original = _store.Users["requester@example.com"].PasswordHash;
        await CreateService(SeedUser("requester@example.com", "different", "user")).SeedAsync();

        _store.Users.Should().ContainSingle();
        _store.Users["requester@example.com"].PasswordHash.Should().Be(original);
    }

    [Fact]
    public async Task Authenticate_SeededUserWithRightPassword_ReturnsPrincipalWithRoles()
    {
        var service = CreateService(SeedUser("admin", "Ch@nageM3", "admin", "user"));

        var principal = await service.AuthenticateAsync("ADMIN", "Ch@nageM3");

        principal.Should().NotBeNull();
        principal!.Username.Should().Be("admin");
        principal.Roles.Should().Equal("admin", "user");
    }

    [Fact]
    public async Task Authenticate_WrongPasswordOrUnknownUser_ReturnsNull()
    {
        var service = CreateService(SeedUser("admin", "Ch@nageM3", "admin"));

        (await service.AuthenticateAsync("admin", "nope")).Should().BeNull();
        (await service.AuthenticateAsync("ghost", "Ch@nageM3")).Should().BeNull();
    }

    [Fact]
    public async Task Session_CreateThenValidate_ReturnsThePrincipal_AndStoresOnlyAHashOfTheToken()
    {
        var service = CreateService();
        var (token, expires) = await service.CreateSessionAsync(new PocPrincipal("admin", ["admin"]));

        var principal = await service.ValidateSessionAsync(token);

        principal.Should().NotBeNull();
        principal!.Username.Should().Be("admin");
        expires.Should().Be(_time.GetUtcNow().AddHours(8));
        _store.Sessions.Keys.Should().NotContain(token);
        _store.Sessions.Keys.Should().Contain(PocAuthService.HashToken(token));
    }

    [Fact]
    public async Task Session_TokensAreUniqueAndUrlSafe()
    {
        var service = CreateService();
        var first = (await service.CreateSessionAsync(new PocPrincipal("a", []))).Token;
        var second = (await service.CreateSessionAsync(new PocPrincipal("a", []))).Token;

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
    }

    [Fact]
    public async Task Session_AfterExpiry_IsRejectedAndRemoved()
    {
        var service = CreateService();
        var (token, _) = await service.CreateSessionAsync(new PocPrincipal("admin", ["admin"]));

        _time.Advance(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1));

        (await service.ValidateSessionAsync(token)).Should().BeNull();
        _store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_JustBeforeExpiry_IsStillValid()
    {
        var service = CreateService();
        var (token, _) = await service.CreateSessionAsync(new PocPrincipal("admin", ["admin"]));

        _time.Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));

        (await service.ValidateSessionAsync(token)).Should().NotBeNull();
    }

    [Fact]
    public async Task Session_RevokedOrUnknownOrEmpty_IsRejected()
    {
        var service = CreateService();
        var (token, _) = await service.CreateSessionAsync(new PocPrincipal("admin", ["admin"]));

        await service.RevokeSessionAsync(token);

        (await service.ValidateSessionAsync(token)).Should().BeNull();
        (await service.ValidateSessionAsync("never-issued")).Should().BeNull();
        (await service.ValidateSessionAsync(null)).Should().BeNull();
        (await service.ValidateSessionAsync("")).Should().BeNull();
    }

    [Fact]
    public void BuildSessionCookie_HasHttpOnlyLaxAndPocPrefix_SecureOnlyWhenRequested()
    {
        var now = _time.GetUtcNow();

        var http = PocAuthService.BuildSessionCookie("tok", now.AddHours(8), now, secure: false);
        var https = PocAuthService.BuildSessionCookie("tok", now.AddHours(8), now, secure: true);

        http.Should().StartWith("poc_session=tok; ").And.Contain("HttpOnly").And.Contain("SameSite=Lax")
            .And.Contain("Path=/").And.Contain("Max-Age=28800").And.NotContain("Secure");
        https.Should().Contain("; Secure");
    }

    [Theory]
    [InlineData("http://localhost/api/x", null, false)]
    [InlineData("http://localhost/api/x", "https", true)]
    [InlineData("http://localhost/api/x", "https, http", true)]
    [InlineData("http://localhost/api/x", "http, https", false)]
    [InlineData("https://localhost/api/x", null, true)]
    [InlineData("https://localhost/api/x", "http", true)]
    public void IsHttps_EitherSchemeOrForwardedProtoCounts(string url, string? forwardedProto, bool expected)
    {
        var req = Functions.TestHttpRequestData.CreateRequest(url: url);
        if (forwardedProto is not null)
        {
            req.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        PocAuthService.IsHttps(req).Should().Be(expected);
    }

    [Fact]
    public async Task Authenticate_UserWithMalformedStoredHash_ReturnsNull()
    {
        _store.Users["bad"] = new PocUserEntity { RowKey = "bad", Username = "bad", Salt = "%%%", PasswordHash = "%%%", Iterations = 1_000 };

        (await CreateService().AuthenticateAsync("bad", "pw")).Should().BeNull();
    }

    [Fact]
    public void BuildClearedCookie_UsesMaxAgeZero()
    {
        PocAuthService.BuildClearedCookie(secure: false).Should().StartWith("poc_session=; ").And.Contain("Max-Age=0");
    }
}
