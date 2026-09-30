using App.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Api.PocAuth;

public static class PocAuthServiceCollectionExtensions
{
    public static IServiceCollection AddPocAuth(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPocAuthStore>(_ => new TablePocAuthStore(StorageConnection.ResolveConnectionString()));
        services.AddSingleton(sp => new PocAuthService(sp.GetRequiredService<IPocAuthStore>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
