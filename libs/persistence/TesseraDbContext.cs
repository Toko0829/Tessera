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
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<WatchProgress> WatchProgresses => Set<WatchProgress>();

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

        builder.Entity<Video>(video =>
        {
            video.HasKey(v => v.Id);

            // Stored as text so the status reads plainly in the database.
            video.Property(v => v.Status).HasConversion<string>().HasMaxLength(32);
            video.Property(v => v.Title).HasMaxLength(256);
            video.Property(v => v.OriginalFileName).HasMaxLength(256);
            video.Property(v => v.ContentType).HasMaxLength(128);
            video.Property(v => v.StorageKey).HasMaxLength(512);

            // Listing a user's own videos queries by owner.
            video.HasIndex(v => v.OwnerId);

            video.HasOne(v => v.Owner)
                .WithMany()
                .HasForeignKey(v => v.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WatchProgress>(progress =>
        {
            // One row per user per video, and the composite key is also the lookup
            // path (every read and upsert is by both columns), so no further index.
            progress.HasKey(p => new { p.UserId, p.VideoId });

            progress.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            progress.HasOne(p => p.Video)
                .WithMany()
                .HasForeignKey(p => p.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
