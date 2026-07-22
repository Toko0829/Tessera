# Web Playback

## Purpose

The player page: loads a video's details, plays its HLS ladder with hls.js, and
tells the user honestly when playback is not possible (not ready, unsupported
browser, failure). The library links every Ready video here.

**Not responsible for:** authorising playback (the API does, per request), or
choosing renditions (hls.js adapts on its own).

## Public API

| Name | Kind | Responsibility |
| --- | --- | --- |
| `Watch` | lazy route component (guarded) | fetch the video, drive hls.js, resume and save progress, error states |
| `VideoService.get(id)` | service method | one video's details, including duration and saved position |
| `VideoService.saveProgress(id, seconds)` | service method | record the playback position |
| `attachPlaybackToken` | function | adds the bearer token to hls.js requests, same-origin only |
| `resumePosition` | function | where playback starts given the saved position |

Route: `/watch/:id`, lazy loaded behind `authGuard`. hls.js ships only inside this
chunk, keeping the initial bundle within budget (measured 75 kB gzipped against the
200 kB ceiling).

## Data Model

None persisted. Transient signals: the video, a load error, a playback error, and
an unsupported-browser flag.

## Dependencies

**Depends on:** the playback API (`/videos/{id}/hls/*`), the videos API
(`GET /videos/{id}`), hls.js 1.x, `AuthService` for the token.
**Depended on by:** the home library links here.

hls.js is the one player dependency the charter names; it performs its own XHRs,
which is why token handling lives here and not in the interceptor.

## Security

- **Token routing:** hls.js requests bypass the Angular interceptor, so
  `attachPlaybackToken` applies the same rule in the player: the bearer token goes
  only to our own origin, never to the storage host segment redirects land on.
  Tested for the relative, same-origin, cross-origin, and no-token cases.
- **The UI enforces nothing:** the API authorises every playlist and segment
  request; this page only renders outcomes.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Video missing or not the caller's | `GET /videos/{id}` errors | back to the library | load error message |
| Video not Ready | status on the detail response | wait, come back | waiting message with the status |
| Browser without Media Source Extensions | `Hls.isSupported()` false | use an MSE browser | honest unsupported message |
| Fatal stream error mid-play | hls.js fatal error event | reload the page | playback error message |
| Access token expires mid-play | API returns 401 to hls.js | reload (session restore refreshes) | playback error message |
| A progress save fails | request errors | the next cadence tick retries | nothing; playback is never interrupted over it |
| Tab closes without a final save | nothing to detect | at most one cadence interval is lost | resume lands up to 5 seconds early |

## Testing

**Covered:** `attachPlaybackToken` attaches to relative and same-origin URLs and
never to the storage host or without a token; `resumePosition` starts over with no
save or a save inside the final stretch and resumes anywhere else;
`VideoService.saveProgress` sends the position to the right endpoint; the
component loads the video by route id and shows the title, shows the waiting
state for a non-Ready video, the unsupported state where MSE is absent (jsdom, so
that branch is the natural one), and the load-error state. Real playback, resume
at the saved position, and the library progress bar were verified against the
running stack in a real browser.

**Deliberately not covered:** simulated MSE playback in jsdom (it would test a
mock of hls.js, not playback) and the Playwright E2E, which lands with the wider
E2E setup.

## Decisions

### hls.js everywhere it is supported; no native HLS fallback yet

**Chose:** require Media Source Extensions and decline other browsers with a clear
message.
**Over:** falling back to native HLS (`video.src = master.m3u8`) on iOS Safari.
**Because:** native HLS fetches media without our `Authorization` header, so it
cannot pass the API's per-request authorisation. Serving those browsers needs
credentialed CDN delivery (signed cookies), which arrives with the CloudFront
work; a broken player would be dishonest in the meantime.
**Trade-off accepted:** no iPhone playback until CDN-signed delivery lands,
recorded as a known gap.

### Native video controls

**Chose:** the browser's built-in `<video controls>`.
**Over:** custom player chrome.
**Because:** native controls are fully keyboard operable and screen-reader
announced out of the box (charter section 11); custom chrome would re-implement
that for styling gains this slice does not need.
**Trade-off accepted:** less visual polish; revisited when captions and quality
selection UI arrive.

### Progress saved on a 5-second cadence, on pause, and on leaving

**Chose:** throttled `timeupdate` saves plus a save on `pause` and on component
destroy, with a failed save recovered by the next tick.
**Over:** `navigator.sendBeacon` on unload, or saving on every `timeupdate`.
**Because:** a beacon cannot carry the `Authorization` header the API requires, so
the reliable close-tab save is not available to a bearer-token client; the cadence
bounds the loss to 5 seconds, which resume rounds down anyway. Saving unthrottled
would send four requests a second for no added fidelity and trip the rate limit.
**Trade-off accepted:** a hard tab close can lose up to one interval of progress.

### Restart near the end instead of resuming

**Chose:** a saved position inside the final 10 seconds restarts from the top.
**Over:** always resuming exactly where the viewer left.
**Because:** a viewer who reached the credits and comes back wants to watch again,
not to resume into the last seconds and immediately finish.
**Trade-off accepted:** the 10-second window is a product judgement, kept in one
tested function.

## Known gaps (next slices)

- **Captions:** no subtitle tracks exist yet; hls.js will surface them once the
  pipeline produces them. Mandatory before this feature is called complete
  (charter section 11).
- **iOS Safari** playback via CDN signed cookies, with the CloudFront work.
