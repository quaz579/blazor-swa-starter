namespace App.Api.PocAuth;

public enum PasswordUpdateResult
{
    Updated,
    Conflict,
    NotFound,
}

public interface IPocAuthStore
{
    Task<PocUserEntity?> GetUserAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> AddUserIfMissingAsync(PocUserEntity user, CancellationToken cancellationToken = default);

    Task PutSessionAsync(PocSessionEntity session, CancellationToken cancellationToken = default);

    Task<PocSessionEntity?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<PasswordUpdateResult> UpdatePasswordAsync(PocUserEntity verifiedUser, string salt, string passwordHash, int iterations, CancellationToken cancellationToken = default);

    Task DeleteSessionsForUserAsync(string username, string? exceptTokenHash, CancellationToken cancellationToken = default);
}
