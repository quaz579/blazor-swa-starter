using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace App.Web.Services;

public sealed record PocUser(string Username, string[] Roles);

internal sealed record MeResponse(bool Authenticated, string? Username, string[]? Roles);

internal sealed record ErrorResponse(string? Error);

public sealed class PocAuthState
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChangePasswordTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private bool _initialized;

    public PocAuthState(HttpClient http, bool enabled)
    {
        _http = http;
        Enabled = enabled;
    }

    public event Action? Changed;

    public bool Enabled { get; }

    public PocUser? User { get; private set; }

    public bool IsSignedIn => User is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled || _initialized)
        {
            return;
        }

        _initialized = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        User = await FetchMeAsync(timeout.Token);
        Changed?.Invoke();
    }

    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/auth/login", new { username, password }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return "Invalid username or password.";
        }

        if (!response.IsSuccessStatusCode)
        {
            return $"Sign-in failed with status {(int)response.StatusCode}.";
        }

        try
        {
            User = await response.Content.ReadFromJsonAsync<PocUser>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return "Sign-in failed: the API returned an unexpected response.";
        }

        if (User is null)
        {
            return "Sign-in failed: the API returned an unexpected response.";
        }

        Changed?.Invoke();
        return null;
    }

    public async Task<string?> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ChangePasswordTimeout);
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword }, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                User = null;
                Changed?.Invoke();
                return "Your session has expired. Sign in again.";
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            {
                return await ReadErrorAsync(response, timeout.Token) ?? "The password could not be changed. Check the details and try again.";
            }

            return $"Could not change the password (status {(int)response.StatusCode}). Try again.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return "Could not reach the API. Try again.";
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync("api/auth/logout", content: null, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
        }

        User = null;
        Changed?.Invoke();
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
