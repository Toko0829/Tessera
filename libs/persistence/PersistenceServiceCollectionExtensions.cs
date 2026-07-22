using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tessera.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    // Registers the database context against PostgreSQL. The migrations live in
    // this assembly, so point EF at it explicitly.
    public static IServiceCollection AddTesseraPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TesseraDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(TesseraDbContext).Assembly.FullName)));

        return services;
    }
}
