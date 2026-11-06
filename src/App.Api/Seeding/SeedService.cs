using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Api.Seeding;

public sealed record SeedResult(
    [property: JsonPropertyName("seed_version")] string SeedVersion,
    Dictionary<string, int> Tables, Dictionary<string, int> Blobs, string[] Stores);

public sealed class SeedService : BackgroundService
{
    private readonly Lazy<ISeedData> _seed;
    private readonly Lazy<ISeedStore[]> _stores;
    private readonly ILogger<SeedService> _logger;
    private volatile bool _ready;

    public SeedService(ISeedData seed, IEnumerable<ISeedStore> stores, ILogger<SeedService> logger)
        : this(() => seed, () => stores, logger) { }

    internal SeedService(Func<ISeedData> seed, Func<IEnumerable<ISeedStore>> stores, ILogger<SeedService> logger)
    {
        _seed = new(seed);
        _stores = new(() => stores().ToArray());
        _logger = logger;
    }

    public bool Ready => _ready;
    public string Version => _seed.Value.Version;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ResetGate.Shared.WriteAsync(() => _seed.Value.EnsureSeededAsync(stoppingToken), stoppingToken);
            _ready = true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Startup seed failed; readiness remains unavailable");
        }
    }

    public Task<SeedResult?> ResetAsync(CancellationToken cancellationToken) => ResetGate.Shared.TryResetAsync(async () =>
    {
        var stores = _stores.Value;
        foreach (var store in stores) await store.ClearAsync(cancellationToken);
        await _seed.Value.EnsureSeededAsync(cancellationToken);
        var tables = new Dictionary<string, int>();
        var blobs = new Dictionary<string, int>();
        foreach (var store in stores)
            (store.Kind == "table" ? tables : blobs).Add(store.Name, await store.CountAsync(cancellationToken));
        return new SeedResult(Version, tables, blobs,
            stores.Select(store => $"{store.Kind}:{store.Name}").Order(StringComparer.Ordinal).ToArray());
    }, cancellationToken);
}

public static class SeedRegistration
{
    public static IServiceCollection AddSeedContract(this IServiceCollection services)
    {
        services.TryAddSingleton<ISeedData, EmptySeedData>();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, SeedRequestOriginFilter>();
        services.AddSingleton(provider => new SeedService(
            () => provider.GetRequiredService<ISeedData>(),
            () => provider.GetServices<ISeedStore>(),
            provider.GetRequiredService<ILogger<SeedService>>()));
        services.AddHostedService(provider => provider.GetRequiredService<SeedService>());
        return services;
    }
}
