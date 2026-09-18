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
    internal static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? LocalDevelopmentConnectionString;
}
