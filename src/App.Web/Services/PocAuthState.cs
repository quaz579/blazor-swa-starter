using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace App.Web.Services;

public sealed record PocUser(string Username, string[] Roles);

internal sealed record LoginResponse(string? Username, string?[]? Roles);

internal sealed record MeResponse(bool? Authenticated, string? Username, string?[]? Roles);

internal sealed record ErrorResponse(string? Error);

internal sealed record WarningResponse(string? Warning);

public sealed record PasswordChangeResult(string? Error, string? Warning);

public sealed class PocAuthState
{
    private readonly TimeSpan _probeTimeout;
    private readonly TimeSpan _retryBackoff;
    private readonly TimeProvider _timeProvider;
    private readonly object _initializationGate = new();
    private bool _initialized;
    private const string UnconfirmedRevocationWarning = "Password changed, but sign-out of other devices could not be confirmed.";
    private const string ConflictFallback = "The password was changed by another request. Enter the new current password and try again.";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private Task? _initialization;
    private int _revision;

    public PocAuthState(HttpClient http, bool enabled, TimeSpan? probeTimeout = null,
        TimeProvider? timeProvider = null, TimeSpan? retryBackoff = null)
    {
        _http = http;
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(25);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retryBackoff = retryBackoff ?? TimeSpan.FromSeconds(1);
        Enabled = enabled;
    }

    public event Action? Changed;

    public bool Enabled { get; }

    public PocUser? User { get; private set; }

    public bool IsSignedIn => User is not null;

    public bool ProbeFailed { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initializationGate)
        {
            if (!Enabled)
            {
                return Task.CompletedTask;
            }

            if (_initialization is not null)
            {
                return _initialization;
            }

            if (_initialized)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = completion.Task;
            _ = InitializeCoreAsync(completion, cancellationToken);
            return completion.Task;
        }
    }

    private async Task InitializeCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        var revision = _revision;
        Exception? failure = null;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (revision != _revision || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    using var deadline = new CancellationTokenSource(_probeTimeout, _timeProvider);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                    var user = await FetchMeAsync(timeout.Token);
                    if (revision == _revision)
                    {
                        SetUser(user);
                    }
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
                {
                    if (cancellationToken.IsCancellationRequested || revision != _revision)
                    {
                        return;
                    }
                    if (attempt == 0)
                    {
                        await Task.Delay(_retryBackoff, _timeProvider, cancellationToken);
                    }
                }
            }

            if (revision == _revision)
            {
                ProbeFailed = true;
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_initializationGate)
            {
                _initialization = null;
            }
            if (failure is null)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(failure);
            }
        }
    }

    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var revision = ++_revision;
        using var deadline = new CancellationTokenSource(CommandTimeout, _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
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

            LoginResponse? identity;
            try
            {
                identity = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: timeout.Token);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return "Sign-in failed: the API returned an unexpected response.";
            }

            var user = CreateValidatedUser(identity?.Username, identity?.Roles);
            if (user is null)
            {
                return "Sign-in failed: the API returned an unexpected response.";
            }

            if (revision == _revision)
            {
                SetUser(user);
            }
            return null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return "Could not reach the API. Try again.";
        }
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var revision = ++_revision;
        using var deadline = new CancellationTokenSource(CommandTimeout, _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword }, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new(null, await ReadWarningAsync(response, timeout.Token));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (revision == _revision)
                {
                    SetUser(null);
                }
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
        var revision = ++_revision;
        using var deadline = new CancellationTokenSource(CommandTimeout, _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var response = await _http.PostAsync("api/auth/logout", content: null, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return $"Sign-out failed with status {(int)response.StatusCode}. Try again.";
            }

            if (revision == _revision)
            {
                SetUser(null);
            }
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
        ProbeFailed = false;
        _initialized = true;
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
            return string.IsNullOrWhiteSpace(body?.Warning) ? UnconfirmedRevocationWarning : body.Warning;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return UnconfirmedRevocationWarning;
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

    private static PocUser? CreateValidatedUser(string? username, string?[]? roles)
    {
        if (string.IsNullOrWhiteSpace(username) || roles is null)
        {
            return null;
        }

        var validatedRoles = new string[roles.Length];
        for (var index = 0; index < roles.Length; index++)
        {
            var role = roles[index];
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }
            validatedRoles[index] = role;
        }

        return new PocUser(username, validatedRoles);
    }

    private async Task<PocUser?> FetchMeAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/auth/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: cancellationToken)
            ?? throw new JsonException("Missing session response.");
        if (me.Authenticated is false)
        {
            return null;
        }

        return me.Authenticated is true
            ? CreateValidatedUser(me.Username, me.Roles) ?? throw new JsonException("Invalid session identity.")
            : throw new JsonException("Missing authentication status.");
    }
}
