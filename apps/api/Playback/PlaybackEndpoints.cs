using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Api.Auth;
using Tessera.Domain;
using Tessera.Persistence;
using Tessera.Storage;

namespace Tessera.Api.Playback;

// Serves a Ready video's HLS ladder to its owner. Playlists (small text files) are
// streamed through the API so their relative references resolve back to these same
// routes; each segment request redirects to a short-lived presigned storage URL,
// minted at request time so the 5-minute expiry holds for any video length. In
// production a CDN takes over the segment path (CLAUDE.md section 6); these routes
// are the pre-CDN and local-development delivery path.
public static partial class PlaybackEndpoints
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    // Exactly the files the worker writes: master.m3u8, v{n}_index.m3u8, and
    // v{n}_seg{nnn}.ts. Anything else is refused before storage is touched, so these
    // routes cannot be used to read arbitrary keys.
    [GeneratedRegex(@"^(master|v\d{1,2}_index)\.m3u8$")]
    private static partial Regex PlaylistName();

    [GeneratedRegex(@"^v\d{1,2}_seg\d{1,5}\.ts$")]
    private static partial Regex SegmentName();

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/videos/{id:guid}/hls").RequireAuthorization();

        group.MapGet("/{playlist}.m3u8", GetPlaylistAsync)
            .RequireRateLimiting(RateLimitPolicies.PlaybackManifest);
        group.MapGet("/{segment}.ts", GetSegmentAsync)
            .RequireRateLimiting(RateLimitPolicies.PlaybackSegment);

        return app;
    }

    private static async Task<IResult> GetPlaylistAsync(
        Guid id,
        string playlist,
        ClaimsPrincipal principal,
        TesseraDbContext db,
        IObjectStorage storage,
        CancellationToken ct)
    {
        var fileName = $"{playlist}.m3u8";
        if (!PlaylistName().IsMatch(fileName))
        {
            return Results.NotFound();
        }

        var denied = await AuthorizePlaybackAsync(id, principal, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        var bytes = await storage.ReadAllBytesAsync(HlsPaths.Key(id, fileName), ct);
        if (bytes is null)
        {
            // A well-formed name for a rendition this video does not have (e.g. a
            // v5 playlist when the ladder has two rungs).
            return Results.NotFound();
        }

        return Results.Bytes(bytes, PlaylistContentType);
    }

    private static async Task<IResult> GetSegmentAsync(
        Guid id,
        string segment,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        TesseraDbContext db,
        IObjectStorage storage,
        IOptions<PlaybackOptions> options,
        CancellationToken ct)
    {
        var fileName = $"{segment}.ts";
        if (!SegmentName().IsMatch(fileName))
        {
            return Results.NotFound();
        }

        var denied = await AuthorizePlaybackAsync(id, principal, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        // No storage round trip to check existence: segment names come from playlists
        // this API served, and a fabricated name just yields a 404 from storage.
        var url = storage.CreatePresignedGetUrl(HlsPaths.Key(id, fileName), options.Value.SegmentUrlExpiry);

        // The Location header carries a signed URL; no cache may store it.
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Redirect(url, permanent: false);
    }

    // The ownership gate for everything under /hls: the caller must own the video and
    // it must be Ready. Returns null when playback may proceed.
    private static async Task<IResult?> AuthorizePlaybackAsync(
        Guid id, ClaimsPrincipal principal, TesseraDbContext db, CancellationToken ct)
    {
        var ownerId = principal.UserId();
        if (ownerId is null)
        {
            return Results.Unauthorized();
        }

        var video = await db.Videos
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new { v.OwnerId, v.Status })
            .FirstOrDefaultAsync(ct);
        if (video is null)
        {
            return Results.NotFound();
        }

        if (video.OwnerId != ownerId)
        {
            return Results.Forbid();
        }

        if (video.Status != VideoStatus.Ready)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This video is not ready to play.");
        }

        return null;
    }
}
