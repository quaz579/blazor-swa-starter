using System.Text.Json;
using Azure.Storage.Blobs;

namespace App.Api.Storage;

/// <summary>
/// Resolves the Azure Storage connection string used by every <see cref="BlobJsonStore{T}"/>
/// registration. Internal (not a DI service) so it stays a plain, greppable function call —
/// see <see cref="ServiceCollectionExtensions.AddBlobJsonStore{T}"/> for how it is used.
/// </summary>
internal static class StorageConnection
{
    internal const string LocalDevelopmentConnectionString = "UseDevelopmentStorage=true";

    /// <summary>
    /// Precedence: an explicit <c>AZURE_STORAGE_CONNECTION_STRING</c>, then the Functions
    /// host's own <c>AzureWebJobsStorage</c>, then the Azurite emulator connection string.
    /// </summary>
    internal static string ResolveConnectionString() => Resolve().ConnectionString;

    internal static (string Mode, string ConnectionString) Resolve(
        string? markerPath = null, Func<string, string?>? getSetting = null)
    {
        markerPath ??= Path.Combine(AppContext.BaseDirectory, "storage-mode.json");
        getSetting ??= Environment.GetEnvironmentVariable;
        if (File.Exists(markerPath))
        {
            try
            {
                using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
                if (marker.RootElement.GetProperty("mode").GetString() != "preview")
                    throw new InvalidOperationException("Unknown storage mode in storage-mode.json.");
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidOperationException("Invalid storage-mode.json: expected mode preview.", exception);
            }

            var preview = getSetting("PREVIEW_STORAGE_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(preview))
                throw new MissingPreviewStorageException();
            return ("preview", preview);
        }

        return ("production", getSetting("AZURE_STORAGE_CONNECTION_STRING")
            ?? getSetting("AzureWebJobsStorage")
            ?? LocalDevelopmentConnectionString);
    }

    internal static (string Mode, string Account) Identity()
    {
        var storage = Resolve();
        return (storage.Mode, new BlobServiceClient(storage.ConnectionString).AccountName);
    }
}

internal sealed class MissingPreviewStorageException : InvalidOperationException
{
    internal MissingPreviewStorageException()
        : base("Preview storage requires PREVIEW_STORAGE_CONNECTION_STRING; production fallback is forbidden.")
    {
    }
}
