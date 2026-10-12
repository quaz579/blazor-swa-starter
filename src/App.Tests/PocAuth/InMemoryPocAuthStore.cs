using App.Api.PocAuth;

namespace App.Tests.PocAuth;

internal sealed class InMemoryPocAuthStore : IPocAuthStore
{
    public Dictionary<string, PocUserEntity> Users { get; } = new();
    public Dictionary<string, PocSessionEntity> Sessions { get; } = new();

    public bool ThrowOnSessionDelete { get; set; }

    public Task<PocUserEntity?> GetUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = Users.GetValueOrDefault(PocUserEntity.KeyFor(username));
        return Task.FromResult(user is null ? null : new PocUserEntity
        {
            RowKey = user.RowKey,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = user.Roles,
            PasswordHash = user.PasswordHash,
            Salt = user.Salt,
            Iterations = user.Iterations,
            ETag = user.ETag,
        })!;
    }

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

    public Task<PasswordUpdateResult> UpdatePasswordAsync(PocUserEntity verifiedUser, string passwordHash, string salt, int iterations, CancellationToken cancellationToken = default)
    {
        if (!Users.TryGetValue(verifiedUser.RowKey, out var user))
        {
            return Task.FromResult(PasswordUpdateResult.NotFound);
        }

        if (user.ETag != verifiedUser.ETag)
        {
            return Task.FromResult(PasswordUpdateResult.Conflict);
        }

        user.PasswordHash = passwordHash;
        user.Salt = salt;
        user.Iterations = iterations;
        user.ETag = new Azure.ETag(Guid.NewGuid().ToString("N"));
        return Task.FromResult(PasswordUpdateResult.Updated);
    }

    public Task DeleteSessionsForUserAsync(string username, string? exceptTokenHash, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSessionDelete)
        {
            throw new InvalidOperationException("session store down");
        }

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
