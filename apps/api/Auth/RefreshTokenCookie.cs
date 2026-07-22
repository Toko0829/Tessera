using Microsoft.Extensions.Hosting;

namespace Tessera.Api.Auth;

// The refresh token lives only in this cookie: HttpOnly (JavaScript cannot read it),
// SameSite=Strict (not sent on cross-site requests), and scoped to /auth so it never
// rides along on other routes. Secure is on everywhere except local development,
// where the app is served over http on localhost.
public sealed class RefreshTokenCookie(IHostEnvironment environment)
{
    public const string Name = "tessera_refresh";

    private readonly bool _secure = !environment.IsDevelopment();

    public void Set(HttpResponse response, string value, DateTimeOffset expiresAt)
        => response.Cookies.Append(Name, value, BuildOptions(expiresAt));

    public void Clear(HttpResponse response)
        => response.Cookies.Delete(Name, BuildOptions(DateTimeOffset.UnixEpoch));

    private CookieOptions BuildOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = _secure,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expiresAt,
        IsEssential = true,
    };
}
