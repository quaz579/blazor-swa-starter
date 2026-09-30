using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace App.Web.Services;

public sealed record PocUser(string Username, string[] Roles);

public sealed class PocAuthState
{
    private const string SignedInHintKey = "poc_signed_in";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private bool _initialized;

    public PocAuthState(HttpClient http, IJSRuntime js, bool enabled)
    {
        _http = http;
        _js = js;
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
        var hint = await _js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, SignedInHintKey);
        if (hint is null)
        {
            return;
        }

        User = await FetchMeAsync(cancellationToken);
        if (User is null)
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, SignedInHintKey);
        }

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

        await _js.InvokeVoidAsync("localStorage.setItem", cancellationToken, SignedInHintKey, "1");
        Changed?.Invoke();
        return null;
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
        await _js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, SignedInHintKey);
        Changed?.Invoke();
    }

    private async Task<PocUser?> FetchMeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("api/auth/me", cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<PocUser>(cancellationToken: cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
