using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tessera.Persistence;

// The application's database context. Extends Identity's context so the user,
// role, and claim tables are created and managed by Identity. Domain-specific
// tables (videos, watch progress) are added here as their modules land.
public sealed class TesseraDbContext(DbContextOptions<TesseraDbContext> options)
    : IdentityDbContext<TesseraUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(token =>
        {
            token.HasKey(t => t.Id);

            // Unique so a hash lookup returns at most one row, and it is the query path.
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.HasIndex(t => t.TokenHash).IsUnique();

            // Revoking a whole family on reuse queries by FamilyId.
            token.HasIndex(t => t.FamilyId);

            token.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
