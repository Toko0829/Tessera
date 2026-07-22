namespace Tessera.Api.Videos;

public sealed record InitiateUploadRequest(string Title, string FileName, string ContentType, long SizeBytes);

public sealed record InitiateUploadResponse(
    Guid VideoId,
    string UploadUrl,
    IReadOnlyDictionary<string, string> Fields);

public sealed record VideoResponse(Guid Id, string Title, string Status, DateTimeOffset CreatedAt);
