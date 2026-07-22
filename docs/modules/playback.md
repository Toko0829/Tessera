# Playback

## Purpose

Serves a Ready video's HLS ladder to its owner and records where they stopped
watching: playlists stream through the API, each segment request redirects to a
short-lived signed storage URL, and watch progress is written back per user per
video. It is the delivery and viewing-state path between the transcode output and
the player.

**Not responsible for:** producing the ladder (transcode), the player UI
(web-playback), or storage mechanics (storage). It decides who may play what, and
hands out access one file at a time.

## Public API

| Method | Path | Auth | Rate limit | Success | Failure |
| --- | --- | --- | --- | --- | --- |
| GET | `/videos/{id}/hls/master.m3u8`, `/videos/{id}/hls/v{n}_index.m3u8` | Bearer | 60 / min per user | `200`, `application/vnd.apple.mpegurl`, playlist bytes | `401`, `403` not owner, `404` unknown name or missing object, `409` not Ready |
| GET | `/videos/{id}/hls/v{n}_seg{nnn}.ts` | Bearer | 300 / min per user | `302` to a presigned storage GET, 5 min expiry, `Cache-Control: no-store` | `401`, `403`, `404`, `409` as above |
| PUT | `/videos/{id}/progress` | Bearer | 120 / min per user | `204` | `400` position outside the video, `401`, `403` not owner, `404`, `409` not Ready |

File names are validated against the exact layout the worker writes (`master.m3u8`,
`v{n}_index.m3u8`, `v{n}_seg{nnn}.ts`); anything else is refused before storage is
touched, so these routes cannot read arbitrary keys.

## Data Model

Owns `WatchProgresses`: `UserId` (FK, cascade), `VideoId` (FK, cascade),
`PositionSeconds`, `UpdatedAt`. Composite primary key `(UserId, VideoId)`, which is
also the only lookup path (every read and upsert is by both columns), so it needs
no further index; the `VideoId` index exists for the cascade when a video is
deleted. Reads `Videos` (owner, status, and `DurationSeconds`, which the worker
records at Ready).

The storage layout it serves, `videos/{videoId}/hls/*`, is defined once in
`Tessera.Domain.HlsPaths` and shared with the worker that writes it.

## Dependencies

**Depends on:** `libs/persistence` (ownership and status), `libs/storage`
(playlist reads, presigned GET URLs), `libs/domain` (`HlsPaths`), Redis for the
rate limiters.
**Depended on by:** the web-playback module drives these endpoints through hls.js.

## Security

- **Authentication:** every route requires a valid bearer token.
- **Authorisation:** the caller must own the video; anyone else gets `403`. Tested
  for both playlists and segments.
- **Input validation:** the video id is a route-constrained GUID; file names must
  match the ladder layout regexes exactly.
- **Rate limits:** manifests at the charter's 60 per minute per user; segments at
  the general authenticated tier, 300 per minute per user (normal playback is about
  15 segment requests per minute, so the ceiling only bites on abuse); progress
  writes at the charter's 120 per minute per user.
- **Progress validation:** the position must be between zero and the video's
  measured duration; the write is refused for videos that are not Ready. Videos
  transcoded before duration capture existed have no length on record, and only the
  lower bound applies to them.
- **Signed URLs:** segment redirects carry a presigned GET scoped to one object,
  expiring in 5 minutes (charter section 6). The redirect response is
  `Cache-Control: no-store` so no cache can replay a signed URL.
- **Token containment:** the redirect sends the browser to the storage host without
  credentials; browsers strip the `Authorization` header on cross-origin redirects,
  and S3-compatible storage rejects a request carrying both a query signature and
  an auth header, so a leak would fail loudly rather than silently. Verified
  against the running stack.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Video not Ready yet | status check | wait for transcode | `409`, player page shows the status |
| Playlist object missing for a Ready video | storage read returns null | investigate storage; re-transcode | `404`, player error state |
| Signed URL expires before the browser follows it | storage rejects the GET | hls.js retries through the API for a fresh URL | a stall at worst |
| Storage down | SDK throws on playlist read | retry once storage is back | generic `5xx`, playback error state |
| Redis down | limiter fails | restore Redis | `5xx` on playback routes |

## Testing

Integration tests against real PostgreSQL, Redis, and MinIO: the owner streams the
master and variant playlists byte-identical to what was stored; a segment request
returns a `302` whose signed URL actually serves the object's bytes with no further
credentials; a non-owner gets `403` for playlists and segments; anonymous gets
`401`; a Processing video gets `409`; names outside the ladder layout and playlists
that do not exist get `404`; the manifest limiter returns `429` on request 61.

Progress: a save comes back on the detail and the list; the latest write wins; a
non-owner gets `403`; a non-Ready video gets `409`; negative and past-the-end
positions get `400`; the limiter returns `429` on write 121.

**Deliberately not covered:** expiry of the signed URL (would need a 5-minute test
delay); the bound is asserted by inspection of `X-Amz-Expires` in the live check.

## Decisions

### API-served playlists with per-segment redirects, no playlist rewriting

**Chose:** stream playlists through the API untouched and redirect each segment
request to a freshly signed storage URL.
**Over:** rewriting playlists to embed presigned segment URLs, or proxying segment
bytes through the API.
**Because:** playlists reference segments by relative name, so serving them from
the API path makes those references resolve back to the API on their own. hls.js
fetches a VOD playlist once, so URLs embedded at playlist time would expire mid
playback for any video longer than the 5-minute bound; a URL minted per request
never can. Proxying bytes would put video traffic through the API, which the
performance budget forbids.
**Trade-off accepted:** one lightweight authorised redirect per segment (about one
every four seconds per viewer). In production CloudFront takes over segment
delivery and this path remains the origin-side fallback.

### Progress upsert via `INSERT ... ON CONFLICT`

**Chose:** a single parameterised PostgreSQL upsert, last write wins.
**Over:** EF read-then-write, or a unique-violation catch-and-retry.
**Because:** two players can save concurrently (two tabs, two devices); the atomic
upsert cannot lose the row or race the unique key, and the newest position is the
only one worth keeping.
**Trade-off accepted:** one raw (still parameterised) SQL statement in an
otherwise LINQ-only codebase, justified by the atomicity.

### Position validated against a measured duration

**Chose:** have the worker record the ffprobe duration at Ready and validate
progress writes against it.
**Over:** accepting any non-negative number, or a made-up ceiling.
**Because:** the duration is already known during transcode, so the honest bound
costs one probe; an arbitrary cap would be a guess the charter forbids.
**Trade-off accepted:** videos transcoded before this change have no recorded
length and get only the lower bound.

### 409 for a video that is not Ready

**Chose:** `409 Conflict` with a plain title.
**Over:** `404`, hiding the video's existence.
**Because:** the caller has already proven ownership, so there is nothing to hide;
telling the owner "not ready yet" is accurate and actionable.
**Trade-off accepted:** none of note.
