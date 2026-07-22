# Web Video

## Purpose

The client side of video upload: the authenticated library page and the upload
flow that sends a file straight to object storage and records it, with progress.

**Not responsible for:** issuing presigned URLs, validating content, or storing
anything (the video-upload and storage modules do that). It orchestrates the calls
and shows the result.

## Public API

| Name | Kind | Responsibility |
| --- | --- | --- |
| `VideoService.list()` | service method | the caller's videos |
| `VideoService.upload(file)` | service method | reserve, upload to storage, complete; emits progress percent |
| `Home` | route component (guarded) | library list, upload control, progress and status |

Routes: reuses `/home` (guarded). Lazy loaded.

## Data Model

None. Server-owned. The component holds transient signals: the list, the current
upload progress, and an error message.

## Dependencies

**Depends on:** the video-upload API (`POST /videos`, `POST /videos/{id}/complete`,
`GET /videos`), object storage (the browser POSTs directly to the presigned URL),
`AuthService` (via the interceptor for the bearer token).
**Depended on by:** nothing yet; playback will link from the library.

In development the dev server proxies `/videos` (and `/auth`) to the API, and MinIO
is configured to allow cross-origin uploads from the web origin.

## Security

- **Authentication:** every API call carries the bearer token via the interceptor.
- **Token is never sent to storage:** the interceptor only attaches the token to our
  own API (relative URLs), never to the absolute storage URL, so the JWT is not leaked
  to the storage host. Tested.
- **The UI enforces nothing:** the API validates content, ownership, size, and rate
  limits. The `accept="video/*"` on the picker is a convenience only.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Upload or validation fails | the flow errors | pick another file | inline error, upload state cleared |
| Rejected content | API returns 400 at complete | upload a real video | inline error |
| Storage/CORS misconfigured | cross-origin POST fails | fix config | inline error, nothing partially saved in the list |

## Testing

`VideoService` runs the full reserve to upload to complete flow (asserted end to end
with a mocked transport, ending at 100%). The interceptor is proven to attach the
token to the API and to withhold it from an absolute storage URL. The real browser
flow, including the cross-origin upload to MinIO, was verified by hand against the
running stack.

**Deliberately not covered yet:** a Playwright end-to-end that drives the file picker;
it lands with the wider E2E setup.

## Decisions

### The service owns the whole upload flow

**Chose:** `VideoService.upload(file)` performs initiate, the direct storage upload,
and complete, emitting progress.
**Over:** orchestrating the three calls in the component.
**Because:** the sequence is one logical operation with one failure story; keeping it
in the service leaves the component to show state (progress, errors) as signals.
**Trade-off accepted:** the service touches raw upload events, but only to turn them
into a progress percentage.
