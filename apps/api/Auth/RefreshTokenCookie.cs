namespace Tessera.Api.Auth;

// The refresh token lives only in this cookie: HttpOnly (JavaScript cannot read it),
// Secure (HTTPS only), SameSite=Strict (not sent on cross-site requests), and scoped
// to /auth so it never rides along on other routes.
public static class RefreshTokenCookie
{
    public const string Name = "tessera_refresh";

    public static void Set(HttpResponse response, string value, DateTimeOffset expiresAt)
        => response.Cookies.Append(Name, value, BuildOptions(expiresAt));

    public static void Clear(HttpResponse response)
        => response.Cookies.Delete(Name, BuildOptions(DateTimeOffset.UnixEpoch));

    private static CookieOptions BuildOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expiresAt,
        IsEssential = true,
    };
}
