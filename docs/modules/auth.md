# Auth

## Purpose

Registers accounts and authenticates users, issuing a short-lived JWT access token
on successful login. Applies the brute-force controls of the charter: per-IP rate
limiting and per-account lockout.

**Not responsible for:** storing users (that is the persistence module), refresh
tokens and session continuity (the next slice), or authorising access to specific
resources by ownership (that lands with the resources themselves).

## Public API

| Method | Path | Auth | Rate limit | Body | Success | Failure |
| --- | --- | --- | --- | --- | --- | --- |
| POST | `/auth/register` | public | 5 / 15 min per IP | `{ email, password }` | `201 Created` | `400` validation |
| POST | `/auth/login` | public | 5 / 15 min per IP | `{ email, password }` | `200` + `{ accessToken, expiresAt }` | `401` invalid, `429` rate limited |
| GET | `/auth/me` | Bearer token | none | | `200` + `{ id, email }` | `401` no/invalid token |

The access token is a JWT signed HS256, carrying `sub` (user id), `email`, and
`jti`, valid for 15 minutes.

## Data Model

No tables of its own. Reads and writes the Identity user tables owned by the
persistence module. Rate-limit counters live in Redis (fixed-window keys
`auth-register:{ip}` and `auth-login:{ip}`), not in a table.

## Dependencies

**Depends on:** `libs/persistence` (user store), ASP.NET Core Identity
(`UserManager`, `SignInManager`), JWT bearer authentication, Redis via
`RedisRateLimiting` for the limiter.
**Depended on by:** every future authenticated endpoint, which will require a valid
token issued here.

## Security

- **Authentication:** login verifies the password through Identity; protected
  endpoints require a valid bearer token, validated for issuer, audience, signature,
  and lifetime.
- **Authorisation:** `/auth/me` requires an authenticated principal. Resource
  ownership checks do not apply yet because no owned resources exist; they arrive
  with those resources and each will be tested for the 403 case (section 6).
- **Input validation:** email format and a 12-character minimum password are checked
  server-side before Identity is touched; Identity enforces its policy as well.
- **Account enumeration:** register and login return one generic message for every
  failure, so an attacker cannot tell whether an email exists.
- **Rate limits:** register and login are limited to 5 per 15 minutes per IP, backed
  by Redis so the limit holds across instances. The limiter runs before
  authentication so it cannot be used as a brute-force oracle.
- **Lockout:** five failed logins lock the account for 15 minutes, independent of the
  IP rate limit.
- **Secrets:** the JWT signing key and connection strings are never committed. In
  development they come from user-secrets; in production from the secret store.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Wrong password | Identity sign-in fails | user retries | `401` generic message |
| Account locked | lockout window active | wait out the window | `401` generic message |
| Too many attempts | limiter rejects | wait for the window | `429` |
| Expired/invalid token | token validation fails | log in again | `401` |
| Database or Redis down | dependency throws at startup or on use | restart once the dependency is back | generic `5xx`, never internals |

## Testing

**Covered** (integration tests against real Postgres and Redis containers):
register then login then call a protected route; weak password rejected; wrong
password returns 401; five failed logins lock the account; `/auth/me` without a token
returns 401; login returns 429 once the window is exhausted.

**Deliberately not covered:** Identity's internal hashing and token validation (the
framework's responsibility). Resource-ownership 403 tests do not exist yet because no
owned resource exists.

## Decisions

### HS256 symmetric signing

**Chose:** a single shared secret (HS256).
**Over:** asymmetric RS256/ES256 with a public/private key pair.
**Because:** there is one API validating its own tokens, so a shared secret is
simplest and there is no third party that needs a public key.
**Trade-off accepted:** if a second service ever needs to validate tokens without
sharing the secret, this moves to asymmetric keys.

### Rate limit keyed by IP, account covered by lockout

**Chose:** limit register and login per IP, and lock accounts via Identity.
**Over:** reading the posted email to build an "IP + account" limiter key.
**Because:** the request body is not available where the limiter partitions, and the
two mechanisms together already cover both dimensions the charter asks for.
**Trade-off accepted:** documented, and revisited if body-aware limiting is needed.

## Known gaps (next slices)

- **Refresh tokens.** Login issues only a 15-minute access token; staying logged in,
  the HttpOnly refresh cookie, rotation, and reuse detection are the next PR.
- **Exponential backoff.** Lockout and rate limiting are in place; per-attempt
  increasing delay is not yet implemented.
- **Real client IP behind the CDN.** The limiter uses the socket IP. Trusting
  forwarded headers from CloudFront is wired with the deployment config, not here.
