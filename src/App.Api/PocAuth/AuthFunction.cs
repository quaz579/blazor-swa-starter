using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace App.Api.PocAuth;

public sealed class AuthFunction
{
    private readonly PocAuthService _auth;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthFunction> _logger;

    public AuthFunction(PocAuthService auth, TimeProvider time, ILogger<AuthFunction> logger)
    {
        _auth = auth;
        _time = time;
        _logger = logger;
    }

    internal sealed record LoginRequest(string? Username, string? Password);

    internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>POST /api/auth/login</summary>
    [Function("PocAuthLogin")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!PocAuthSettings.IsEnabled)
        {
            return await NotEnabledAsync(req, cancellationToken);
        }

        LoginRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<LoginRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected malformed login body");
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Username and password are required." }, cancellationToken);
            return bad;
        }

        var principal = await _auth.AuthenticateAsync(request.Username, request.Password, cancellationToken);
        if (principal is null)
        {
            _logger.LogWarning("Rejected login attempt");
            var denied = req.CreateResponse(HttpStatusCode.Unauthorized);
            await denied.WriteAsJsonAsync(new { error = "Invalid username or password." }, cancellationToken);
            return denied;
        }

        var (token, expires) = await _auth.CreateSessionAsync(principal, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", PocAuthService.BuildSessionCookie(token, expires, _time.GetUtcNow(), PocAuthService.IsHttps(req)));
        await response.WriteAsJsonAsync(new { username = principal.Username, roles = principal.Roles }, cancellationToken);
        return response;
    }

    /// <summary>POST /api/auth/logout</summary>
    [Function("PocAuthLogout")]
    public async Task<HttpResponseData> Logout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/logout")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!PocAuthSettings.IsEnabled)
        {
            return await NotEnabledAsync(req, cancellationToken);
        }

        await _auth.RevokeSessionAsync(PocAuthService.ReadSessionToken(req), cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.NoContent);
        response.Headers.Add("Set-Cookie", PocAuthService.BuildClearedCookie(PocAuthService.IsHttps(req)));
        return response;
    }

    /// <summary>GET /api/auth/me</summary>
    [Function("PocAuthMe")]
    public async Task<HttpResponseData> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!PocAuthSettings.IsEnabled)
        {
            return await NotEnabledAsync(req, cancellationToken);
        }

        var principal = await _auth.GetPrincipalAsync(req, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        if (principal is null)
        {
            await response.WriteAsJsonAsync(new { authenticated = false }, cancellationToken);
            return response;
        }

        await response.WriteAsJsonAsync(new { authenticated = true, username = principal.Username, roles = principal.Roles }, cancellationToken);
        return response;
    }

    /// <summary>POST /api/auth/change-password</summary>
    [Function("PocAuthChangePassword")]
    public async Task<HttpResponseData> ChangePassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/change-password")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!PocAuthSettings.IsEnabled)
        {
            return await NotEnabledAsync(req, cancellationToken);
        }

        var principal = await _auth.GetPrincipalAsync(req, cancellationToken);
        if (principal is null)
        {
            return await PocAuthService.UnauthorizedAsync(req, cancellationToken);
        }

        ChangePasswordRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<ChangePasswordRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected malformed change-password body");
            request = null;
        }

        if (request is null || string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return await BadRequestAsync(req, "Current password and new password are required.", cancellationToken);
        }

        var outcome = await _auth.ChangePasswordAsync(
            principal, PocAuthService.ReadSessionToken(req), request.CurrentPassword, request.NewPassword, cancellationToken);
        switch (outcome.Result)
        {
            case ChangePasswordResult.Changed:
                if (outcome.Message is null)
                {
                    return req.CreateResponse(HttpStatusCode.NoContent);
                }

                _logger.LogWarning("{Message}", outcome.Message);
                var partial = req.CreateResponse(HttpStatusCode.OK);
                await partial.WriteAsJsonAsync(new { warning = outcome.Message }, cancellationToken);
                return partial;
            case ChangePasswordResult.WrongCurrentPassword:
                _logger.LogWarning("Rejected change-password attempt with a wrong current password");
                return await BadRequestAsync(req, outcome.Message!, cancellationToken);
            case ChangePasswordResult.WeakNewPassword:
                return await BadRequestAsync(req, outcome.Message!, cancellationToken);
            case ChangePasswordResult.Conflict:
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = outcome.Message }, cancellationToken);
                return conflict;
            default:
                return await PocAuthService.UnauthorizedAsync(req, cancellationToken);
        }
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string message, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> NotEnabledAsync(HttpRequestData req, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.NotFound);
        await response.WriteAsJsonAsync(new { error = "Authentication is not enabled." }, cancellationToken);
        return response;
    }
}
