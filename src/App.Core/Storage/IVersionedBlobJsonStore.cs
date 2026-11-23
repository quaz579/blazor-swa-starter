namespace App.Core.Storage;

public interface IVersionedBlobJsonStore<T> where T : class
{
    Task<Versioned<T>?> GetVersionedAsync(string key, CancellationToken cancellationToken = default);
    Task<VersionedWriteResult> TryCreateAsync(string key, T value, CancellationToken cancellationToken = default);
    Task<VersionedWriteResult> TryReplaceAsync(string key, T value, string expectedETag, CancellationToken cancellationToken = default);
}
