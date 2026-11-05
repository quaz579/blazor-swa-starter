using System.Text.Json;
using Azure.Storage.Blobs;

namespace App.Api.Storage;

internal sealed record ResolvedStorage(
    string Mode,
    string ConnectionString,
    string Account,
    string Identity,
    string? ProductionAccount);

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

    internal static ResolvedStorage Resolve(
        string? markerPath = null, Func<string, string?>? getSetting = null)
    {
        markerPath ??= Path.Combine(AppContext.BaseDirectory, "storage-mode.json");
        getSetting ??= Environment.GetEnvironmentVariable;

        var mode = ReadMode(markerPath);
        var connectionString = mode == "preview"
            ? getSetting("PREVIEW_STORAGE_CONNECTION_STRING")
            : getSetting("AZURE_STORAGE_CONNECTION_STRING")
                ?? getSetting("AzureWebJobsStorage")
                ?? LocalDevelopmentConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (mode == "preview")
                throw new MissingPreviewStorageException();

            throw new StorageConnectionConfigurationException(
                "The configured storage connection string is empty.", mode);
        }

        string account;
        try
        {
            account = new BlobServiceClient(connectionString).AccountName;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new StorageConnectionConfigurationException(
                "The configured storage connection string is invalid.", mode, exception);
        }

        var productionAccount = getSetting("PRODUCTION_STORAGE_ACCOUNT_NAME");
        var previewAccount = getSetting("PREVIEW_STORAGE_ACCOUNT_NAME");
        var identity = "unverified";
        if (productionAccount is not null || previewAccount is not null)
        {
            if (string.IsNullOrWhiteSpace(productionAccount) || string.IsNullOrWhiteSpace(previewAccount))
                throw new StorageIdentityConfigurationException(
                    "Both expected storage account names must be configured.", mode);

            if (string.Equals(productionAccount, previewAccount, StringComparison.OrdinalIgnoreCase))
                throw new StorageIdentityConfigurationException(
                    "Production and preview storage account names must differ.", mode);

            var expectedAccount = mode == "preview" ? previewAccount : productionAccount;
            if (!string.Equals(account, expectedAccount, StringComparison.OrdinalIgnoreCase))
                throw new StorageIdentityMismatchException(mode);

            identity = "verified";
        }

        return new ResolvedStorage(mode, connectionString, account, identity, productionAccount);
    }

    internal static ResolvedStorage Identity() => Resolve();

    private static string ReadMode(string markerPath)
    {
        try
        {
            using var stream = File.OpenRead(markerPath);
            using var marker = JsonDocument.Parse(stream);
            if (marker.RootElement.GetProperty("mode").GetString() == "preview")
                return "preview";
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return "production";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidStorageModeException(exception);
        }

        throw new InvalidStorageModeException();
    }
}

internal class StorageConfigurationException : InvalidOperationException
{
    internal string StorageMode { get; }

    internal StorageConfigurationException(string message, string storageMode, Exception? innerException = null)
        : base(message, innerException)
    {
        StorageMode = storageMode;
    }
}

internal sealed class MissingPreviewStorageException : StorageConfigurationException
{
    internal MissingPreviewStorageException()
        : base("Preview storage requires PREVIEW_STORAGE_CONNECTION_STRING; production fallback is forbidden.", "preview")
    {
    }
}

internal sealed class InvalidStorageModeException : StorageConfigurationException
{
    internal InvalidStorageModeException(Exception? innerException = null)
        : base("Invalid storage-mode.json: expected mode preview.", "unknown", innerException)
    {
    }
}

internal sealed class StorageConnectionConfigurationException : StorageConfigurationException
{
    internal StorageConnectionConfigurationException(string message, string storageMode, Exception? innerException = null)
        : base(message, storageMode, innerException)
    {
    }
}

internal sealed class StorageIdentityConfigurationException : StorageConfigurationException
{
    internal StorageIdentityConfigurationException(string message, string storageMode)
        : base(message, storageMode)
    {
    }
}

internal sealed class StorageIdentityMismatchException : StorageConfigurationException
{
    internal StorageIdentityMismatchException(string storageMode)
        : base("The resolved storage account does not match the expected account for this mode.", storageMode)
    {
    }
}
