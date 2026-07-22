using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tessera.Persistence;

// The application's database context. Extends Identity's context so the user,
// role, and claim tables are created and managed by Identity. Domain-specific
// tables (videos, watch progress) are added here as their modules land.
public sealed class TesseraDbContext(DbContextOptions<TesseraDbContext> options)
    : IdentityDbContext<TesseraUser, IdentityRole<Guid>, Guid>(options);
