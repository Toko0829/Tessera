# Storage

## Purpose

Wraps S3-compatible object storage behind a small interface: create a presigned
upload, read an object's first bytes, get its size, delete it, and (in development)
ensure the bucket exists. The same code targets MinIO locally and AWS S3 in
production.

**Not responsible for:** deciding what may be uploaded or by whom (that is the
video-upload module), or any database state.

## Public API

`IObjectStorage`:

| Member | Returns | Notes |
| --- | --- | --- |
| `CreatePresignedUpload(key, contentType, maxBytes, expiry)` | `PresignedUpload` (url + form fields) | presigned S3 POST with a `content-length-range` |
| `ReadHeadAsync(key, byteCount, ct)` | `byte[]` | first bytes, for magic-byte checks |
| `GetSizeAsync(key, ct)` | `long?` | object size, or null if absent |
| `CreatePresignedGetUrl(key, expiry)` | `string` | signed GET URL for one object; playback segment redirects |
| `ReadAllBytesAsync(key, ct)` | `byte[]?` | whole small object (HLS playlists), or null if absent |
| `DeleteAsync(key, ct)` | `Task` | remove an object |
| `EnsureBucketExistsAsync(ct)` | `Task` | development only |

## Data Model

None. State lives in object storage, not a database.

## Dependencies

**Depends on:** AWSSDK.S3. **Depended on by:** `apps/api` (video-upload), and the
transcode worker later.

## Security

- **Credentials:** static access/secret keys from configuration; the secret comes
  from user-secrets locally and the secret store in production, never source.
- **Presigned POST:** the POST policy is signed by hand (the .NET SDK has no helper),
  scoped to one key with a size ceiling, short expiry. The signing is exercised by a
  real upload test in the video-upload suite.
- **No public buckets:** objects are never served directly from here; playback will go
  through signed CDN URLs (a later module).

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Object missing | `GetSizeAsync` returns null / read throws | caller handles (e.g. 400) | handled by the caller |
| Storage unreachable | SDK throws | retry once back | caller surfaces a generic error |

## Testing

Exercised through the video-upload integration tests against a real MinIO container:
the presigned POST is used for a real upload, and header reads and deletes run against
stored objects. No standalone unit tests; a mocked S3 would only test the mock.

## Decisions

### Hand-signed presigned POST

**Chose:** build and sign the S3 POST policy directly with SigV4.
**Over:** presigned PUT via the SDK's `GetPreSignedURL`.
**Because:** POST's `content-length-range` is the only way to make storage enforce the
size limit; the SDK has no presigned-POST helper.
**Trade-off accepted:** a small amount of careful crypto code, covered by a real
upload test rather than trusted blind.
