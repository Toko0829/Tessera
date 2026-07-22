namespace Tessera.Storage;

// A browser-ready presigned POST: the form action URL and the fields to send with the
// file. The browser posts multipart/form-data with these fields plus the file last.
public sealed record PresignedUpload(string Url, IReadOnlyDictionary<string, string> Fields);

public interface IObjectStorage
{
    // A presigned POST scoped to exactly one key, with a size ceiling the storage
    // itself enforces (content-length-range), valid for the given window.
    PresignedUpload CreatePresignedUpload(string key, string contentType, long maxBytes, TimeSpan expiry);

    // Reads the first bytes of an object, for magic-byte validation.
    Task<byte[]> ReadHeadAsync(string key, int byteCount, CancellationToken ct);

    // The stored object's size, or null if it does not exist.
    Task<long?> GetSizeAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);

    // Downloads an object to a local file; the worker pulls originals this way.
    Task DownloadToFileAsync(string key, string filePath, CancellationToken ct);

    // Uploads a local file; the worker pushes transcode outputs this way.
    Task UploadFileAsync(string key, string filePath, string contentType, CancellationToken ct);

    // Creates the bucket if it is missing. Used for local development only.
    Task EnsureBucketExistsAsync(CancellationToken ct);
}
