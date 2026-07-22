namespace Tessera.Persistence;

public enum VideoStatus
{
    // Row created, waiting for the browser to upload to storage.
    PendingUpload,

    // Upload finished and passed magic-byte validation; queued for transcode.
    Uploaded,

    // Upload failed validation (wrong content, size mismatch); object removed.
    Rejected,

    // The worker is transcoding it right now.
    Processing,

    // Transcoded renditions exist in storage; playable.
    Ready,

    // Transcoding failed (bad stream, timeout); the original is kept for diagnosis.
    Failed,
}

public sealed class Video
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }

    public required string Title { get; set; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }

    // The one object key this upload is scoped to, e.g. uploads/{ownerId}/{videoId}.
    public required string StorageKey { get; init; }

    public VideoStatus Status { get; set; }

    // Measured by the worker's ffprobe pass; null until the video is transcoded.
    public double? DurationSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public TesseraUser? Owner { get; init; }
}
