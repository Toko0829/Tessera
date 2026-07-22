# Web Auth

## Purpose

The client side of authentication: the login and signup screens, the authenticated
landing, and the in-browser auth state. It calls the auth API and holds the access
token in memory for the current session.

**Not responsible for:** enforcing access (the API does that on every request), or
storing users and tokens (the backend owns that). Anything here only hides or shows
UI.

## Public API

| Name | Kind | Responsibility |
| --- | --- | --- |
| `AuthService` | injectable service | register, login, logout, refresh, restore session, `me`; exposes `isAuthenticated` and the in-memory token |
| app initializer | bootstrap hook | calls `restoreSession()` once on startup, before the first route resolves |
| `authInterceptor` | HTTP interceptor | attaches the bearer token to outgoing requests |
| `authGuard` | route guard | redirects unauthenticated visitors to `/login` |
| `Login` / `Signup` | lazy route components | the auth forms |
| `Home` | lazy route component (guarded) | authenticated landing, shows the signed-in email |

Routes: `/login`, `/signup` (public), `/home` (guarded). All are lazy loaded.

## Data Model

None persisted. The access token lives in a signal in `AuthService`, in memory only.
The refresh token is an HttpOnly cookie the browser manages; this code never reads it.

## Dependencies

**Depends on:** the auth API (`/auth/register`, `/auth/login`, `/auth/refresh`,
`/auth/logout`, `/auth/me`), Angular reactive forms, router.
**Depended on by:** every future authenticated screen, which will sit behind
`authGuard` and rely on the interceptor for its token.

In development the dev server proxies `/auth` to the API (see `proxy.conf.json`), so
the SPA and API are same-origin and no CORS is needed locally.

## Security

- **Token storage:** the access token is kept in a signal, never in `localStorage` or
  `sessionStorage`, so a cross-site script cannot read it (CLAUDE.md section 6).
- **Refresh token:** delivered and sent as an HttpOnly, Secure, SameSite=Strict
  cookie; the calls that use it set `withCredentials`.
- **The guard is not a control:** it only decides what UI to show. The API authorises
  every request on its own.
- **Generic errors:** the forms show one message on failure and never reveal whether
  an email exists.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Wrong credentials | API returns 401 | retry | inline "not right" message |
| Weak password / bad input | API returns 400 | fix and resubmit | inline error on signup |
| API unreachable | request errors | retry later | inline error, no crash |
| Page reload | in-memory token is gone | silent refresh from the cookie on startup | stays signed in; only bounced to `/login` if the refresh cookie is gone or expired |

## Testing

**Covered:** `AuthService` stores the token on login, clears it on logout, restores
it from the refresh cookie, and stays logged out when there is no cookie (with
`withCredentials` asserted); the `Login` component logs in and navigates on success
and shows an error on failure; the root component renders. The register to login to
`/home` flow and a page reload keeping the session were verified by hand against the
running API.

**Deliberately not covered yet:** a Playwright end-to-end test of the browser flow.
The charter mandates E2E for upload to playback; an auth E2E rides in with that setup.

## Decisions

### Signals for state, RxJS for the HTTP calls

**Chose:** the access token and form state are signals; the API calls are RxJS
streams.
**Over:** an RxJS `BehaviorSubject` as the state container.
**Because:** the charter is explicit that signals hold state and RxJS handles
asynchronous streams. A single readable signal for "am I logged in" is simpler than a
subject to subscribe to.
**Trade-off accepted:** none of note at this size.

### Access token in memory, refresh token in a cookie

**Chose:** hold the short-lived access token in memory and lean on the HttpOnly
refresh cookie for continuity.
**Over:** persisting the access token in `localStorage`.
**Because:** a token in `localStorage` is readable by any injected script; in memory
plus an HttpOnly cookie is the safer split the charter asks for.
**Trade-off accepted:** the in-memory token is lost on reload, so an app initializer
silently calls `/auth/refresh` on startup to restore it from the cookie before the
first route resolves.

### Refresh cookie is Secure everywhere except local development

**Chose:** set the cookie `Secure` in every environment except `Development`.
**Over:** always `Secure` (which needs HTTPS in dev), or never `Secure`.
**Because:** the browser will not store a `Secure` cookie over plain http, so an
always-Secure cookie breaks the reload flow in local dev. Dev runs on http on
localhost, which is not network-exposed, so dropping `Secure` there is safe; every
real environment keeps it.
**Trade-off accepted:** dev and production differ in this one cookie attribute; the
difference is environment-gated in one place on the API.

## Known gaps (next slices)

- **Playwright E2E** for the browser flow, landing with the wider E2E setup.
