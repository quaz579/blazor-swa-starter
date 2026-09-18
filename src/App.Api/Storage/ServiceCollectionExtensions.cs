using App.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Api.Storage;

/// <summary>
/// DI helpers for registering <see cref="IBlobJsonStore{T}"/> implementations.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="BlobJsonStore{T}"/> backed by the shared <c>app-data</c>
    /// container, storing blobs under <c>{prefix}/{key}.json</c>. Call once per <typeparamref name="T"/>
    /// to add a new collection (e.g. <c>services.AddBlobJsonStore&lt;Note&gt;("notes")</c>).
    /// </summary>
    public static IServiceCollection AddBlobJsonStore<T>(this IServiceCollection services, string prefix)
        where T : class
    {
        services.AddSingleton(new BlobJsonStoreOptions<T> { Prefix = prefix });
        services.AddSingleton<IBlobJsonStore<T>>(sp =>
        {
            var options = sp.GetRequiredService<BlobJsonStoreOptions<T>>();
            var logger = sp.GetRequiredService<ILogger<BlobJsonStore<T>>>();
            return new BlobJsonStore<T>(StorageConnection.ResolveConnectionString(), options, logger);
        });

        return services;
    }
}
