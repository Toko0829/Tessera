using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tessera.Persistence;

// Used only by the `dotnet ef` tooling at design time so migrations can be generated
// without booting the API. `migrations add` builds the model offline and never opens
// this connection, so the value only needs to parse — it points at no real database
// and carries no password.
public sealed class TesseraDbContextFactory : IDesignTimeDbContextFactory<TesseraDbContext>
{
    public TesseraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseNpgsql("Host=localhost;Database=tessera_designtime;Username=tessera")
            .Options;

        return new TesseraDbContext(options);
    }
}
