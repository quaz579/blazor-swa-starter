using App.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.Security;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFieldEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        var registration = FieldEncryptionConfiguration.Resolve(configuration);
        var status = new FieldEncryptionStatus(registration.Mode);
        services.AddSingleton(status);
        services.AddSingleton<IFieldProtector>(provider =>
        {
            status.LogDevelopmentWarning(provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AesGcmFieldProtector>>());
            return new AesGcmFieldProtector(registration.KeyRing);
        });
        return services;
    }
}
