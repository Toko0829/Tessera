namespace Tessera.Persistence;

// A refresh token, stored only as a hash so a database leak does not expose usable
// tokens. Tokens issued from one login share a FamilyId; presenting an already-rotated
// token revokes the whole family (reuse detection).
public sealed class RefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid FamilyId { get; init; }

    // SHA-256 of the raw token, hex-encoded. The raw token is never persisted.
    public required string TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    // Set when the token is rotated or explicitly revoked (logout, reuse).
    public DateTimeOffset? RevokedAt { get; set; }

    // The token that superseded this one, for auditing the rotation chain.
    public Guid? ReplacedByTokenId { get; set; }

    public TesseraUser? User { get; init; }
}
