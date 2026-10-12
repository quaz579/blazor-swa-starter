using App.Api.PocAuth;

namespace App.Tests.PocAuth;

internal sealed class InMemoryPocAuthStore : IPocAuthStore
{
    public Dictionary<string, PocUserEntity> Users { get; } = new();
    public Dictionary<string, PocSessionEntity> Sessions { get; } = new();

    public Task<PocUserEntity?> GetUserAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.GetValueOrDefault(PocUserEntity.KeyFor(username)));

    public Task<bool> AddUserIfMissingAsync(PocUserEntity user, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.TryAdd(user.RowKey, user));

    public Task PutSessionAsync(PocSessionEntity session, CancellationToken cancellationToken = default)
    {
        Sessions[session.RowKey] = session;
        return Task.CompletedTask;
    }

    public Task<PocSessionEntity?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sessions.GetValueOrDefault(tokenHash));

    public Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        Sessions.Remove(tokenHash);
        return Task.CompletedTask;
    }

    public Task<bool> UpdatePasswordAsync(string username, string passwordHash, string salt, int iterations, CancellationToken cancellationToken = default)
    {
        if (!Users.TryGetValue(PocUserEntity.KeyFor(username), out var user))
        {
            return Task.FromResult(false);
        }

        user.PasswordHash = passwordHash;
        user.Salt = salt;
        user.Iterations = iterations;
        return Task.FromResult(true);
    }

    public Task DeleteSessionsForUserAsync(string username, string? exceptTokenHash, CancellationToken cancellationToken = default)
    {
        foreach (var key in Sessions.Where(s => s.Value.Username == username && s.Key != exceptTokenHash).Select(s => s.Key).ToList())
        {
            Sessions.Remove(key);
        }

        return Task.CompletedTask;
    }
}

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
