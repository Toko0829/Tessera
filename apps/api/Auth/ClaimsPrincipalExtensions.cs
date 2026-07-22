using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Tessera.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    // The authenticated user's id from the token's `sub` claim, or null if absent.
    public static Guid? UserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
