using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tessera.Persistence;
using Tessera.Queue;
using Tessera.Storage;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Tessera.Worker.Tests;

// Real PostgreSQL, Redis, and MinIO containers plus the worker's own service
// registrations, so the transcode tests exercise the actual stack (CLAUDE.md
// section 8). ffmpeg itself is the real binary, resolvable via TESSERA_FFMPEG /
// TESSERA_FFPROBE when it is not on PATH.
public sealed class WorkerFixture : IAsyncLifetime
{
    private const string MinioUser = "tessera";
    private const string MinioPassword = "tessera_dev_minio";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio")
        .WithUsername(MinioUser)
        .WithPassword(MinioPassword)
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public static string ToolPath(string tool)
        => Environment.GetEnvironmentVariable($"TESSERA_{tool.ToUpperInvariant()}") ?? tool;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _minio.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Bucket"] = "tessera-test",
                ["Storage:ServiceUrl"] = _minio.GetConnectionString(),
                ["Storage:Region"] = "us-east-1",
                ["Storage:AccessKey"] = MinioUser,
                ["Storage:SecretKey"] = MinioPassword,
                ["Storage:ForcePathStyle"] = "true",
                ["Transcode:FfmpegPath"] = ToolPath("ffmpeg"),
                ["Transcode:FfprobePath"] = ToolPath("ffprobe"),
                ["Transcode:TimeoutMinutes"] = "5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddTesseraPersistence(_postgres.GetConnectionString());
        services.AddTesseraStorage(configuration);
        services.AddSingleton<IConnectionMultiplexer>(
            await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString()));
        services.AddSingleton<TranscodeQueue>();
        services.Configure<TranscodeOptions>(configuration.GetSection(TranscodeOptions.SectionName));
        services.AddSingleton<FfmpegTranscoder>();
        services.AddScoped<TranscodeService>();
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TesseraDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IObjectStorage>().EnsureBucketExistsAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _minio.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
