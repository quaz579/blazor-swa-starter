namespace App.Core.Storage;

/// <summary>Stores <typeparamref name="T"/> values as JSON blobs, one blob per key.</summary>
public interface IBlobJsonStore<T> where T : class
{
    Task<T?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task PutAsync(string key, T value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix = "", CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
