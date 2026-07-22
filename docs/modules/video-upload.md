# Video Upload

## Purpose

Lets an authenticated user upload a video by handing the browser a short-lived
presigned URL so the file goes straight to object storage, then validates the
uploaded file and records it. It is the entry point of the video pipeline.

**Not responsible for:** transcoding (the worker), playback, or the object storage
mechanics themselves (that is the storage module). It decides *what* may be uploaded
and *by whom*, and records the result.

## Public API

| Method | Path | Auth | Rate limit | Body | Success | Failure |
| --- | --- | --- | --- | --- | --- | --- |
| POST | `/videos` | Bearer | 10 / hour per user | `{ title, fileName, contentType, sizeBytes }` | `200` + `{ videoId, uploadUrl, fields }` | `400` validation, `401`, `429` |
| POST | `/videos/{id}/complete` | Bearer | none | | `200` + video | `400` invalid/too big, `403` not owner, `404`, `409` already done |
| GET | `/videos` | Bearer | none | | `200` + the caller's videos | `401` |

The browser POSTs the file to `uploadUrl` with the returned `fields` (a presigned S3
POST), then calls `complete`.

## Data Model

Owns the `Videos` table: `Id`, `OwnerId` (FK, cascade), `Title`, `OriginalFileName`,
`ContentType`, `SizeBytes`, `StorageKey` (`uploads/{ownerId}/{videoId}`), `Status`
(`PendingUpload` / `Uploaded` / `Rejected`, stored as text), `CreatedAt`.

| Index | Columns | Serves which query | Why |
| --- | --- | --- | --- |
| owner index | `OwnerId` | list a user's videos | `GET /videos` filters by owner |

## Dependencies

**Depends on:** `libs/persistence` (the `Videos` table), `libs/storage` (presigned
upload, header read, delete), `libs/domain` (`VideoSignature` magic-byte check),
Redis for the per-user rate limit.
**Depended on by:** the upload UI calls these endpoints. On a successful `complete`,
the video is enqueued for the transcode worker via `libs/queue` (status committed
first, so a failed enqueue leaves a re-queueable `Uploaded` row, never a job with no
record).

## Security

- **Authentication:** every endpoint requires a valid bearer token.
- **Authorisation:** `complete` and the storage key are scoped to the owner; a user
  gets `403` completing someone else's upload, and `GET /videos` only ever returns
  their own. Tested for the unauthorised case.
- **Input validation:** file name required, content type must be `video/*`, size must
  be positive and within the maximum, all checked server-side before a URL is issued.
- **Content validation:** the real check is magic bytes read back from storage at
  `complete` (ISO base media, Matroska, AVI), not the file name or client MIME type.
  A file that fails is deleted and the row marked `Rejected`.
- **Size limits:** enforced at the API (rejects an oversized declared size) and at
  storage (the presigned POST's `content-length-range` makes storage reject it), and
  re-checked from the stored object's real size at `complete`.
- **Rate limit:** 10 initiations per hour per user, Redis-backed.
- **Presigned URLs:** scoped to exactly one key, short expiry (15 min default). The
  API never receives the file bytes during upload.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Not a real video | magic bytes fail at complete | re-upload a real video | `400`, object deleted, row `Rejected` |
| File too large | storage rejects, or size check at complete | upload a smaller file | storage `4xx`, or `400` at complete |
| Completing twice | status already past `PendingUpload` | nothing to do | `409` |
| Wrong owner | ownership check | not applicable | `403` |
| Storage down | SDK throws | retry once storage is back | generic `5xx`, never internals |

## Testing

Integration tests against real PostgreSQL, Redis, and MinIO containers: a real file
uploaded through the presigned POST then completed becomes `Uploaded`; a non-video
upload is rejected; one user cannot complete another's upload (`403`); initiating
without a token is `401`; the list returns only the caller's videos. These exercise
the actual presigned POST signing and the magic-byte read, not mocks.

The browser upload UI and its MinIO CORS configuration now live in the web-video
module.

## Decisions

### Presigned POST, not PUT

**Chose:** presigned POST.
**Over:** the presigned PUT the charter originally named.
**Because:** only POST carries a `content-length-range` condition, which is how the
storage layer itself enforces the size limit the charter also requires. The charter
was updated (§6) to record this.
**Trade-off accepted:** the AWS SDK for .NET has no presigned-POST helper, so the POST
policy is signed by hand in `libs/storage`; it is covered by a real upload test.

### Validate after upload, from storage

**Chose:** read the file's first bytes back from storage at `complete` to validate.
**Over:** validating bytes at the API during upload.
**Because:** with a direct-to-storage upload the API never sees the bytes, so the only
place to inspect real content is after it lands.
**Trade-off accepted:** an invalid object briefly exists in storage before it is
deleted; acceptable, and the object is never served or transcoded until `Uploaded`.
