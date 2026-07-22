using Microsoft.AspNetCore.Identity;

namespace Tessera.Persistence;

// Application user backed by ASP.NET Core Identity. The key is a Guid, not an int,
// so user identifiers are not sequential and cannot be enumerated by an attacker.
public sealed class TesseraUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; init; }
}
