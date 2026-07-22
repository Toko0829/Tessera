namespace Tessera.Api.Videos;

public sealed class VideoUploadOptions
{
    public const string SectionName = "VideoUpload";

    // Two gigabytes. Enforced at the API and, via the presigned POST, at storage.
    public long MaxSizeBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public int UploadUrlExpiryMinutes { get; init; } = 15;

    public TimeSpan UploadUrlExpiry => TimeSpan.FromMinutes(UploadUrlExpiryMinutes);
}
