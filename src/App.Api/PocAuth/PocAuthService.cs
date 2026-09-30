using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;

namespace App.Api.PocAuth;

public sealed class PocAuthService
{
    private static readonly byte[] DummySalt = new byte[PocPasswordHasher.SaltBytes];
    private static readonly string DummyHash = Convert.ToBase64String(new byte[PocPasswordHasher.KeyBytes]);

    private readonly IPocAuthStore _store;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<PocSeedUser> _seed;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    public PocAuthService(IPocAuthStore store, TimeProvider time)
        : this(store, time, PocAuthSeed.Users)
    {
    }

    public PocAuthService(IPocAuthStore store, TimeProvider time, IReadOnlyList<PocSeedUser> seed)
    {
        _store = store;
        _time = time;
        _seed = seed;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (_seeded)
        {
            return;
        }

        await _seedLock.WaitAsync(cancellationToken);
        try
        {
            if (_seeded)
            {
                return;
            }

            foreach (var user in _seed)
            {
                await _store.AddUserIfMissingAsync(new PocUserEntity
                {
                    RowKey = PocUserEntity.KeyFor(user.Username),
                    Username = user.Username.Trim(),
                    DisplayName = user.DisplayName,
                    Roles = string.Join(',', user.Roles),
                    PasswordHash = user.PasswordHash,
                    Salt = user.Salt,
                    Iterations = user.Iterations,
                    Algorithm = PocPasswordHasher.Algorithm,
                }, cancellationToken);
            }

            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public async Task<PocPrincipal?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await SeedAsync(cancellationToken);
        var user = await _store.GetUserAsync(username, cancellationToken);
        if (user is null)
        {
            PocPasswordHasher.Verify(password, Convert.ToBase64String(DummySalt), DummyHash, PocPasswordHasher.DefaultIterations);
            return null;
        }

        return PocPasswordHasher.Verify(password, user.Salt, user.PasswordHash, user.Iterations)
            ? new PocPrincipal(user.Username, user.GetRoles())
            : null;
    }

    public async Task<(string Token, DateTimeOffset ExpiresUtc)> CreateSessionAsync(PocPrincipal principal, CancellationToken cancellationToken = default)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = _time.GetUtcNow().Add(PocAuthSettings.SessionLifetime);
        await _store.PutSessionAsync(new PocSessionEntity
        {
            RowKey = HashToken(token),
            Username = principal.Username,
            Roles = string.Join(',', principal.Roles),
            ExpiresUtc = expires,
        }, cancellationToken);
        return (token, expires);
    }

    public async Task<PocPrincipal?> ValidateSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashToken(token);
        var session = await _store.GetSessionAsync(tokenHash, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresUtc <= _time.GetUtcNow())
        {
            await _store.DeleteSessionAsync(tokenHash, cancellationToken);
            return null;
        }

        return new PocPrincipal(
            session.Username,
            session.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public async Task RevokeSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            await _store.DeleteSessionAsync(HashToken(token), cancellationToken);
        }
    }

    public Task<PocPrincipal?> GetPrincipalAsync(HttpRequestData req, CancellationToken cancellationToken = default) =>
        ValidateSessionAsync(ReadSessionToken(req), cancellationToken);

    public async Task<PocPrincipal?> RequireSessionAsync(HttpRequestData req, string? role = null, CancellationToken cancellationToken = default)
    {
        var principal = await GetPrincipalAsync(req, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        return role is null || principal.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) ? principal : null;
    }

    public static async Task<HttpResponseData> UnauthorizedAsync(HttpRequestData req, CancellationToken cancellationToken = default)
    {
        var response = req.CreateResponse(HttpStatusCode.Unauthorized);
        await response.WriteAsJsonAsync(new { error = "Sign in required." }, cancellationToken);
        return response;
    }

    public static string? ReadSessionToken(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Cookie", out var values))
        {
            return null;
        }

        foreach (var header in values)
        {
            foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq > 0 && part[..eq] == PocAuthSettings.CookieName)
                {
                    return part[(eq + 1)..];
                }
            }
        }

        return null;
    }

    public static bool IsHttps(HttpRequestData req)
    {
        if (req.Url.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return req.Headers.TryGetValues("X-Forwarded-Proto", out var proto)
            && proto.Any(v => v.Split(',')[0].Trim().Equals("https", StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildSessionCookie(string token, DateTimeOffset expiresUtc, DateTimeOffset nowUtc, bool secure)
    {
        var maxAge = Math.Max(0, (int)(expiresUtc - nowUtc).TotalSeconds);
        return BuildCookie(token, maxAge, secure);
    }

    public static string BuildClearedCookie(bool secure) => BuildCookie(string.Empty, 0, secure);

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string BuildCookie(string value, int maxAgeSeconds, bool secure) =>
        $"{PocAuthSettings.CookieName}={value}; Path=/; HttpOnly; SameSite=Lax; Max-Age={maxAgeSeconds}{(secure ? "; Secure" : string.Empty)}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
