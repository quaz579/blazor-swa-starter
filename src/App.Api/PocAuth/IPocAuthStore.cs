namespace App.Api.PocAuth;

public interface IPocAuthStore
{
    Task<PocUserEntity?> GetUserAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> AddUserIfMissingAsync(PocUserEntity user, CancellationToken cancellationToken = default);

    Task PutSessionAsync(PocSessionEntity session, CancellationToken cancellationToken = default);

    Task<PocSessionEntity?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken = default);
}
