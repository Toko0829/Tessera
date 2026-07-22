namespace Tessera.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Bucket { get; init; } = string.Empty;

    // Set for an S3-compatible endpoint like MinIO (e.g. http://localhost:9000).
    // Empty means real AWS S3.
    public string? ServiceUrl { get; init; }

    public string Region { get; init; } = "us-east-1";

    // Static credentials. In production these come from the secret store; MinIO uses
    // its dev root credentials locally.
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;

    // MinIO and most S3-compatible stores need path-style addressing.
    public bool ForcePathStyle { get; init; }
}
