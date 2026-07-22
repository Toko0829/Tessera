namespace Tessera.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    // Symmetric HS256 signing key. Supplied by user-secrets in development and by
    // AWS Secrets Manager in production. Never committed (CLAUDE.md section 6).
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
}
