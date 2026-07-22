using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tessera.Persistence;
using Tessera.Storage;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Tessera.Api.Tests;

// Boots the real API against throwaway PostgreSQL, Redis, and MinIO containers, so the
// integration tests exercise the actual stack (CLAUDE.md section 8).
public sealed class TesseraApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Non-secret placeholder, long enough for HS256, used only to sign test tokens.
    private static readonly string TestSigningKey = new('k', 48);

    private const string MinioUser = "tessera";
    private const string MinioPassword = "tessera_dev_minio";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio")
        .WithUsername(MinioUser)
        .WithPassword(MinioPassword)
        .Build();

    private IConnectionMultiplexer _adminRedis = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so the app's startup auto-migration stays off; this factory
        // is the single migration authority for tests.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Tessera", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Jwt:Issuer", "tessera-tests");
        builder.UseSetting("Jwt:Audience", "tessera-tests");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Storage:Bucket", "tessera-test");
        builder.UseSetting("Storage:ServiceUrl", _minio.GetConnectionString());
        builder.UseSetting("Storage:Region", "us-east-1");
        builder.UseSetting("Storage:AccessKey", MinioUser);
        builder.UseSetting("Storage:SecretKey", MinioPassword);
        builder.UseSetting("Storage:ForcePathStyle", "true");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _minio.StartAsync();

        var redisOptions = ConfigurationOptions.Parse(_redis.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _adminRedis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        await database.Database.MigrateAsync();

        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await storage.EnsureBucketExistsAsync(CancellationToken.None);
    }

    // Clears rate-limit counters so each test starts with a fresh window.
    public async Task ResetRateLimitsAsync()
    {
        foreach (var endpoint in _adminRedis.GetEndPoints())
        {
            await _adminRedis.GetServer(endpoint).FlushAllDatabasesAsync();
        }
    }

    public async Task<bool> IsLockedOutAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TesseraUser>>();
        var user = await users.FindByEmailAsync(email);
        return user is not null && await users.IsLockedOutAsync(user);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _adminRedis.DisposeAsync();
        await _minio.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
