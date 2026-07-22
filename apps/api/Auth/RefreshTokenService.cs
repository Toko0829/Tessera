using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Persistence;

namespace Tessera.Api.Auth;

public enum RefreshOutcome
{
    Invalid,
    Reuse,
    Success,
}

public sealed record RotationResult
{
    public required RefreshOutcome Outcome { get; init; }
    public Guid UserId { get; init; }
    public string? RawToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    public static readonly RotationResult Invalid = new() { Outcome = RefreshOutcome.Invalid };
    public static readonly RotationResult Reuse = new() { Outcome = RefreshOutcome.Reuse };

    public static RotationResult Successful(Guid userId, string rawToken, DateTimeOffset expiresAt)
        => new() { Outcome = RefreshOutcome.Success, UserId = userId, RawToken = rawToken, ExpiresAt = expiresAt };
}

public sealed class RefreshTokenService(TesseraDbContext db, TimeProvider clock, IOptions<JwtOptions> options)
{
    private readonly TimeSpan _lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public async Task<(string RawToken, DateTimeOffset ExpiresAt)> IssueAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var raw = GenerateRawToken();

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.Add(_lifetime),
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return (raw, token.ExpiresAt);
    }

    public async Task<RotationResult> RotateAsync(string rawToken, CancellationToken ct)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == Hash(rawToken), ct);
        if (token is null)
        {
            return RotationResult.Invalid;
        }

        var now = clock.GetUtcNow();

        if (token.RevokedAt is not null)
        {
            // A token that was already rotated is being presented again. That only
            // happens if it was captured, so revoke the whole family.
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            return RotationResult.Reuse;
        }

        if (token.ExpiresAt <= now)
        {
            token.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            return RotationResult.Invalid;
        }

        var newRaw = GenerateRawToken();
        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = Hash(newRaw),
            CreatedAt = now,
            ExpiresAt = now.Add(_lifetime),
        };

        db.RefreshTokens.Add(replacement);
        token.RevokedAt = now;
        token.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(ct);

        return RotationResult.Successful(token.UserId, newRaw, replacement.ExpiresAt);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == Hash(rawToken), ct);
        if (token is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), ct);
        }
    }

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
        => db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    private static string GenerateRawToken() => RandomNumberGenerator.GetHexString(64);

    private static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
