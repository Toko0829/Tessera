using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Api.Auth;
using Tessera.Domain;
using Tessera.Persistence;
using Tessera.Queue;
using Tessera.Storage;

namespace Tessera.Api.Videos;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/videos").RequireAuthorization();

        group.MapPost("", InitiateAsync).RequireRateLimiting(RateLimitPolicies.VideoUpload);
        group.MapPost("/{id:guid}/complete", CompleteAsync).RequireRateLimiting(RateLimitPolicies.AuthenticatedDefault);
        group.MapGet("", ListAsync).RequireRateLimiting(RateLimitPolicies.AuthenticatedDefault);
        group.MapGet("/{id:guid}", GetAsync).RequireRateLimiting(RateLimitPolicies.AuthenticatedDefault);

        return app;
    }

    private static async Task<IResult> InitiateAsync(
        InitiateUploadRequest request,
        ClaimsPrincipal principal,
        TesseraDbContext db,
        IObjectStorage storage,
        IOptions<VideoUploadOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ownerId = principal.UserId();
        if (ownerId is null)
        {
            return Results.Unauthorized();
        }

        var errors = Validate(request, options.Value.MaxSizeBytes);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var videoId = Guid.NewGuid();
        var key = $"uploads/{ownerId}/{videoId}";

        db.Videos.Add(new Video
        {
            Id = videoId,
            OwnerId = ownerId.Value,
            Title = string.IsNullOrWhiteSpace(request.Title) ? request.FileName : request.Title,
            OriginalFileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            StorageKey = key,
            Status = VideoStatus.PendingUpload,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        var upload = storage.CreatePresignedUpload(
            key, request.ContentType, options.Value.MaxSizeBytes, options.Value.UploadUrlExpiry);

        return Results.Ok(new InitiateUploadResponse(videoId, upload.Url, upload.Fields));
    }

    private static async Task<IResult> CompleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        TesseraDbContext db,
        IObjectStorage storage,
        IOptions<VideoUploadOptions> options,
        TranscodeQueue transcodeQueue,
        CancellationToken ct)
    {
        var ownerId = principal.UserId();
        if (ownerId is null)
        {
            return Results.Unauthorized();
        }

        var video = await db.Videos.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (video is null)
        {
            return Results.NotFound();
        }

        // Resource ownership: a logged-in user cannot complete someone else's upload.
        if (video.OwnerId != ownerId)
        {
            return Results.Forbid();
        }

        if (video.Status != VideoStatus.PendingUpload)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This upload is already finished.");
        }

        var size = await storage.GetSizeAsync(video.StorageKey, ct);
        if (size is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file was uploaded.");
        }

        if (size > options.Value.MaxSizeBytes)
        {
            return await RejectAsync(db, storage, video, "The file exceeds the size limit.", ct);
        }

        var head = await storage.ReadHeadAsync(video.StorageKey, VideoSignature.HeaderBytesNeeded, ct);
        if (!VideoSignature.IsSupportedVideo(head))
        {
            return await RejectAsync(db, storage, video, "That file is not a supported video.", ct);
        }

        video.Status = VideoStatus.Uploaded;
        await db.SaveChangesAsync(ct);

        // Hand the video to the transcode worker. The DB status is committed first, so
        // a failed enqueue leaves an Uploaded row that can be re-queued, never a
        // queued job with no record.
        await transcodeQueue.EnqueueAsync(video.Id);

        return Results.Ok(ToResponse(video));
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, TesseraDbContext db, CancellationToken ct)
    {
        var ownerId = principal.UserId();
        if (ownerId is null)
        {
            return Results.Unauthorized();
        }

        var owner = ownerId.Value;
        var videos = await db.Videos
            .Where(v => v.OwnerId == owner)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new VideoResponse(
                v.Id,
                v.Title,
                v.Status.ToString(),
                v.CreatedAt,
                v.DurationSeconds,
                db.WatchProgresses
                    .Where(p => p.VideoId == v.Id && p.UserId == owner)
                    .Select(p => (double?)p.PositionSeconds)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return Results.Ok(videos);
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, TesseraDbContext db, CancellationToken ct)
    {
        var ownerId = principal.UserId();
        if (ownerId is null)
        {
            return Results.Unauthorized();
        }

        var video = await db.Videos.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (video is null)
        {
            return Results.NotFound();
        }

        // Resource ownership: a logged-in user cannot read someone else's video.
        if (video.OwnerId != ownerId)
        {
            return Results.Forbid();
        }

        var position = await db.WatchProgresses
            .AsNoTracking()
            .Where(p => p.UserId == ownerId && p.VideoId == id)
            .Select(p => (double?)p.PositionSeconds)
            .FirstOrDefaultAsync(ct);

        return Results.Ok(ToResponse(video, position));
    }

    private static async Task<IResult> RejectAsync(
        TesseraDbContext db, IObjectStorage storage, Video video, string reason, CancellationToken ct)
    {
        await storage.DeleteAsync(video.StorageKey, ct);
        video.Status = VideoStatus.Rejected;
        await db.SaveChangesAsync(ct);
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: reason);
    }

    private static VideoResponse ToResponse(Video video, double? positionSeconds = null)
        => new(video.Id, video.Title, video.Status.ToString(), video.CreatedAt, video.DurationSeconds, positionSeconds);

    private static Dictionary<string, string[]>? Validate(InitiateUploadRequest request, long maxBytes)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            errors["fileName"] = ["A file name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ContentType) ||
            !request.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            errors["contentType"] = ["A video content type is required."];
        }

        if (request.SizeBytes <= 0)
        {
            errors["sizeBytes"] = ["File size must be greater than zero."];
        }
        else if (request.SizeBytes > maxBytes)
        {
            errors["sizeBytes"] = ["File is larger than the allowed maximum."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
