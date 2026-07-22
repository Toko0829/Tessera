namespace Tessera.Api.Videos;

public sealed record InitiateUploadRequest(string Title, string FileName, string ContentType, long SizeBytes);

public sealed record InitiateUploadResponse(
    Guid VideoId,
    string UploadUrl,
    IReadOnlyDictionary<string, string> Fields);

// DurationSeconds is null until the worker has measured the video; PositionSeconds
// is null until the caller has watched some of it.
public sealed record VideoResponse(
    Guid Id,
    string Title,
    string Status,
    DateTimeOffset CreatedAt,
    double? DurationSeconds,
    double? PositionSeconds);

public sealed record SaveProgressRequest(double PositionSeconds);
