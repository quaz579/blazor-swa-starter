using System.Text.Json;
using App.Core.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace App.Api.Storage;

/// <summary>
/// Stores <typeparamref name="T"/> values as camelCase JSON blobs under
/// <c>{Prefix}/{key}.json</c> in the shared <c>app-data</c> container.
/// To add a second collection (e.g. <c>/api/notes</c>), call
/// <c>services.AddBlobJsonStore&lt;Note&gt;("notes")</c> — see
/// <see cref="App.Api.Storage.ServiceCollectionExtensions.AddBlobJsonStore{T}"/>.
/// </summary>
public sealed class BlobJsonStore<T> : IBlobJsonStore<T> where T : class
{
    private const string ContainerName = "app-data";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BlobContainerClient _containerClient;
    private readonly string _prefix;
    private readonly ILogger<BlobJsonStore<T>> _logger;

    public BlobJsonStore(string connectionString, BlobJsonStoreOptions<T> options, ILogger<BlobJsonStore<T>> logger)
    {
        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _prefix = options.Prefix;
        _logger = logger;
    }

    public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(BlobPathFor(key));

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var download = await blobClient.DownloadContentAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(download.Value.Content.ToString(), SerializerOptions);
    }

    public async Task PutAsync(string key, T value, CancellationToken cancellationToken = default)
    {
        var blobPath = BlobPathFor(key);
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = _containerClient.GetBlobClient(blobPath);
        var json = JsonSerializer.Serialize(value, SerializerOptions);

        await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken: cancellationToken);
        _logger.LogInformation("Stored {Prefix} blob for key {Key}", _prefix, key);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var validatedPrefix = ValidatePrefix(prefix);
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobPrefix = $"{_prefix}/{validatedPrefix}";
        var keys = new List<string>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(
            BlobTraits.None,
            BlobStates.None,
            blobPrefix,
            cancellationToken))
        {
            keys.Add(KeyFromBlobPath(blobItem.Name));
        }

        return keys;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(BlobPathFor(key));
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    private string BlobPathFor(string key)
    {
        ValidateKey(key);
        return $"{_prefix}/{key}.json";
    }

    private string KeyFromBlobPath(string blobName)
    {
        var withoutPrefix = blobName[(_prefix.Length + 1)..];
        return withoutPrefix[..^".json".Length];
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must not be null, empty, or whitespace.", nameof(key));
        }

        if (key.Contains('/') || key.Contains('\\') || key.Contains(".."))
        {
            throw new ArgumentException("Key must not contain path separators or '..'.", nameof(key));
        }
    }

    private static string ValidatePrefix(string prefix)
    {
        if (prefix.Contains('\\') || prefix.Contains(".."))
        {
            throw new ArgumentException("Prefix must not contain '\\' or '..'.", nameof(prefix));
        }

        return prefix;
    }
}
