using StackExchange.Redis;
using Tessera.Persistence;
using Tessera.Queue;
using Tessera.Storage;
using Tessera.Worker;

var builder = Host.CreateApplicationBuilder(args);

var databaseConnection = builder.Configuration.GetConnectionString("Tessera")
    ?? throw new InvalidOperationException(
        "Connection string 'Tessera' is not configured. Set it via user-secrets in development or the secret store in production.");
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException(
        "Connection string 'Redis' is not configured. Set it via user-secrets in development or the secret store in production.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTesseraPersistence(databaseConnection);
builder.Services.AddTesseraStorage(builder.Configuration);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<TranscodeQueue>();
builder.Services.Configure<TranscodeOptions>(builder.Configuration.GetSection(TranscodeOptions.SectionName));
builder.Services.AddSingleton<FfmpegTranscoder>();
builder.Services.AddScoped<TranscodeService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
