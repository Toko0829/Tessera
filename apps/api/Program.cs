using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RedisRateLimiting;
using StackExchange.Redis;
using Tessera.Api.Auth;
using Tessera.Persistence;

var builder = WebApplication.CreateBuilder(args);

var databaseConnection = builder.Configuration.GetConnectionString("Tessera")
    ?? throw new InvalidOperationException(
        "Connection string 'Tessera' is not configured. Set it via user-secrets in development or the secret store in production.");
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException(
        "Connection string 'Redis' is not configured. Set it via user-secrets in development or the secret store in production.");

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey is not configured.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

builder.Services.AddTesseraPersistence(databaseConnection);

builder.Services
    .AddIdentityCore<TesseraUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<TesseraDbContext>()
    .AddSignInManager();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddSingleton<RefreshTokenCookie>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConnection));

static RateLimitPartition<string> AuthPartition(HttpContext httpContext, string prefix, int permitLimit)
{
    var multiplexer = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    return RedisRateLimitPartition.GetFixedWindowRateLimiter(
        $"{prefix}:{clientIp}",
        _ => new RedisFixedWindowRateLimiterOptions
        {
            ConnectionMultiplexerFactory = () => multiplexer,
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(15),
        });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Fixed window keyed by client IP, held in Redis so the limit spans every API
    // instance (CLAUDE.md section 6). The account dimension is covered separately by
    // Identity lockout.
    options.AddPolicy(RateLimitPolicies.AuthRegister, ctx => AuthPartition(ctx, RateLimitPolicies.AuthRegister, 5));
    options.AddPolicy(RateLimitPolicies.AuthLogin, ctx => AuthPartition(ctx, RateLimitPolicies.AuthLogin, 5));
    options.AddPolicy(RateLimitPolicies.AuthRefresh, ctx => AuthPartition(ctx, RateLimitPolicies.AuthRefresh, 30));
});

var app = builder.Build();

// In development the schema is applied on startup so a fresh clone runs right after
// `docker compose up`. Production applies migrations in the deploy pipeline, not here.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
    await database.Database.MigrateAsync();
}

// Rate limiting runs before authentication so the auth endpoints cannot be used as
// a brute-force oracle (CLAUDE.md section 6).
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe: public, unauthenticated, and intentionally not rate limited so
// orchestrators can poll it freely.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();

app.Run();

// Exposed so the integration test host (WebApplicationFactory<Program>) can boot the real app.
public partial class Program;
