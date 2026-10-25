using App.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace App.Api.PocAuth;

public static class PocAuthServiceCollectionExtensions
{
    public static IServiceCollection AddPocAuth(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPocAuthStore>(_ => new TablePocAuthStore(StorageConnection.ResolveConnectionString()));
        services.AddSingleton(sp => new PocAuthService(sp.GetRequiredService<IPocAuthStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<PocAuthService>>()));
        return services;
    }
}
