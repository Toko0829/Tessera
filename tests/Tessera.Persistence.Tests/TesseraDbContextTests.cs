using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Tessera.Persistence.Tests;

// Runs against a real PostgreSQL in a throwaway container (CLAUDE.md §8 — a mocked
// database only tests the mock). Proves the migration builds the schema and a user
// round-trips through it.
public sealed class TesseraDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private TesseraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new TesseraDbContext(options);
    }

    [Fact]
    public async Task Migration_creates_schema_and_user_round_trips()
    {
        await using (var ctx = CreateContext())
        {
            await ctx.Database.MigrateAsync();
        }

        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        await using (var ctx = CreateContext())
        {
            ctx.Users.Add(new TesseraUser
            {
                Id = userId,
                UserName = "tornike",
                NormalizedUserName = "TORNIKE",
                Email = "tornike@tessera.test",
                NormalizedEmail = "TORNIKE@TESSERA.TEST",
                CreatedAt = createdAt,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var user = await ctx.Users.SingleAsync(u => u.Id == userId);

            Assert.Equal("tornike", user.UserName);
            Assert.Equal("tornike@tessera.test", user.Email);
            Assert.Equal(createdAt, user.CreatedAt, TimeSpan.FromSeconds(1));
        }
    }
}
