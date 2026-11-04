using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Api.Seeding;

public sealed record SeedResult(
    [property: JsonPropertyName("seed_version")] string SeedVersion,
    Dictionary<string, int> Tables, Dictionary<string, int> Blobs, string[] Stores);

public sealed class SeedService(ISeedData seed, IEnumerable<ISeedStore> stores, ILogger<SeedService> logger) : BackgroundService
{
    private readonly ISeedStore[] _stores = stores.ToArray();
    private volatile bool _ready;
    public bool Ready => _ready;
    public string Version => seed.Version;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ResetGate.Shared.WriteAsync(() => seed.EnsureSeededAsync(stoppingToken), stoppingToken);
            _ready = true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Startup seed failed; readiness remains unavailable");
        }
    }

    public Task<SeedResult?> ResetAsync(CancellationToken cancellationToken) => ResetGate.Shared.TryResetAsync(async () =>
    {
        foreach (var store in _stores) await store.ClearAsync(cancellationToken);
        await seed.EnsureSeededAsync(cancellationToken);
        var tables = new Dictionary<string, int>();
        var blobs = new Dictionary<string, int>();
        foreach (var store in _stores)
            (store.Kind == "table" ? tables : blobs).Add(store.Name, await store.CountAsync(cancellationToken));
        return new SeedResult(seed.Version, tables, blobs,
            _stores.Select(store => $"{store.Kind}:{store.Name}").Order(StringComparer.Ordinal).ToArray());
    }, cancellationToken);
}

public static class SeedRegistration
{
    public static IServiceCollection AddSeedContract(this IServiceCollection services)
    {
        services.TryAddSingleton<ISeedData, EmptySeedData>();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, SeedRequestOriginFilter>();
        services.AddSingleton<SeedService>();
        services.AddHostedService(provider => provider.GetRequiredService<SeedService>());
        return services;
    }
}
