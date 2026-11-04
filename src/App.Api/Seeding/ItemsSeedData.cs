using App.Core.Models;
using App.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.Seeding;

public sealed class ItemsSeedData(IBlobJsonStore<Item> store) : ISeedData
{
    public string Version => "1";

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        foreach (var (id, name, seconds) in new[]
        {
            ("fixture-middle", "Fixture Middle", 60),
            ("fixture-newest", "Fixture Newest", 30),
            ("fixture-oldest", "Fixture Oldest", 90)
        })
            await store.PutAsync(id, new Item
            {
                Id = id, Name = name,
                CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z").AddSeconds(-seconds)
            }, cancellationToken);
    }

    public static void Register(IServiceCollection services)
    {
        if (!AppEnvironment.IsProduction)
            services.AddSingleton<ISeedData, ItemsSeedData>();
    }
}
