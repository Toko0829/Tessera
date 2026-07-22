using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tessera.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddTesseraStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        return services;
    }
}
