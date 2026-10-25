using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace App.Web.Services;

public sealed record PocUser(string Username, string[] Roles);

internal sealed record MeResponse(bool Authenticated, string? Username, string[]? Roles);

internal sealed record ErrorResponse(string? Error);

internal sealed record WarningResponse(string? Warning);

public sealed record PasswordChangeResult(string? Error, string? Warning);

public sealed class PocAuthState
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private const string ConflictFallback = "The password was changed by another request. Enter the new current password and try again.";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private Task? _initialization;
    private int _revision;

    public PocAuthState(HttpClient http, bool enabled)
    {
        _http = http;
        Enabled = enabled;
    }

    public event Action? Changed;

    public bool Enabled { get; }

    public PocUser? User { get; private set; }

    public bool IsSignedIn => User is not null;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Enabled ? _initialization ??= InitializeCoreAsync(cancellationToken) : Task.CompletedTask;

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var revision = _revision;
        var user = await FetchMeAsync(timeout.Token);
        if (revision == _revision)
        {
            SetUser(user);
        }
    }

    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/login", new { username, password }, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return "Invalid username or password.";
            }

            if (!response.IsSuccessStatusCode)
            {
                return $"Sign-in failed with status {(int)response.StatusCode}.";
            }

            PocUser? user;
            try
            {
                user = await response.Content.ReadFromJsonAsync<PocUser>(cancellationToken: timeout.Token);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return "Sign-in failed: the API returned an unexpected response.";
            }

            if (user is null || string.IsNullOrWhiteSpace(user.Username) || user.Roles is null)
            {
                return "Sign-in failed: the API returned an unexpected response.";
            }

            SetUser(user);
            return null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return "Could not reach the API. Try again.";
        }
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword }, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new(null, await ReadWarningAsync(response, timeout.Token));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SetUser(null);
                return new("Your session has expired. Sign in again.", null);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new(await ReadErrorAsync(response, timeout.Token) ?? ConflictFallback, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return new(await ReadErrorAsync(response, timeout.Token) ?? "The password could not be changed. Check the details and try again.", null);
            }

            return new($"Could not change the password (status {(int)response.StatusCode}). Try again.", null);
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new("Could not reach the API. Try again.", null);
        }
    }

    public async Task<string?> LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            using var response = await _http.PostAsync("api/auth/logout", content: null, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return $"Sign-out failed with status {(int)response.StatusCode}. Try again.";
            }

            SetUser(null);
            return null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return "Could not reach the API. Try again.";
        }
    }

    private void SetUser(PocUser? user)
    {
        _revision++;
        User = user;
        Changed?.Invoke();
    }

    private static async Task<string?> ReadWarningAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<WarningResponse>(cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(body?.Warning) ? null : body.Warning;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(body?.Error) ? null : body.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<PocUser?> FetchMeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("api/auth/me", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: cancellationToken);
            return me is { Authenticated: true, Username: not null }
                ? new PocUser(me.Username, me.Roles ?? [])
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
