namespace App.Api.Storage;

/// <summary>
/// Maps a logical collection of <typeparamref name="T"/> onto blob paths within the shared
/// <c>app-data</c> container. Register one instance per <c>T</c> at DI setup time — see
/// <see cref="BlobJsonStore{T}"/> for how <see cref="Prefix"/> is applied.
/// </summary>
public sealed class BlobJsonStoreOptions<T> where T : class
{
    /// <summary>Folder prefix under the container, e.g. <c>"items"</c> for blob paths like <c>items/{key}.json</c>.</summary>
    public required string Prefix { get; init; }
}
