# Auth

## Purpose

Registers accounts and authenticates users, issuing a short-lived JWT access token
plus a rotating refresh token so sessions outlive the access token. Applies the
brute-force controls of the charter: per-IP rate limiting and per-account lockout.

**Not responsible for:** storing users (that is the persistence module) or
authorising access to specific resources by ownership (that lands with the resources
themselves).

## Public API

| Method | Path | Auth | Rate limit | Body | Success | Failure |
| --- | --- | --- | --- | --- | --- | --- |
| POST | `/auth/register` | public | 5 / 15 min per IP | `{ email, password }` | `201 Created` | `400` validation |
| POST | `/auth/login` | public | 5 / 15 min per IP | `{ email, password }` | `200` + `{ accessToken, expiresAt }` and a refresh cookie | `401` invalid, `429` rate limited |
| POST | `/auth/refresh` | refresh cookie | 30 / 15 min per IP | | `200` + `{ accessToken, expiresAt }` and a rotated cookie | `401` invalid/reused |
| POST | `/auth/logout` | refresh cookie | 30 / 15 min per IP | | `204` | `204` even without a cookie |
| GET | `/auth/me` | Bearer token | none | | `200` + `{ id, email }` | `401` no/invalid token |

The access token is a JWT signed HS256, carrying `sub` (user id), `email`, and
`jti`, valid for 15 minutes. The refresh token is an opaque random value returned
only in the `tessera_refresh` cookie, valid for 14 days.

## Data Model

Owns the `RefreshTokens` table; reads and writes the Identity user tables owned by
the persistence module. Rate-limit counters live in Redis (fixed-window keys
`auth-register:{ip}`, `auth-login:{ip}`, `auth-refresh:{ip}`), not in a table.

`RefreshTokens` columns: `Id`, `UserId` (FK, cascade delete), `FamilyId`,
`TokenHash` (SHA-256 hex, unique index), `CreatedAt`, `ExpiresAt`, `RevokedAt`,
`ReplacedByTokenId`. Unique index on `TokenHash` (the lookup path); index on
`FamilyId` (family revocation on reuse).

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
- **Refresh tokens:** stored only as a SHA-256 hash, so a database leak exposes no
  usable token. Delivered in an HttpOnly, SameSite=Strict cookie scoped to `/auth`, so
  JavaScript cannot read it and it does not ride cross-site requests. The cookie is
  `Secure` in every environment except local development (where the app runs on http
  on localhost). Each refresh rotates the token; presenting an already-rotated token
  revokes the whole family, which contains the damage if a token is captured.
- **Secrets:** the JWT signing key and connection strings are never committed. In
  development they come from user-secrets; in production from the secret store.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Wrong password | Identity sign-in fails | user retries | `401` generic message |
| Account locked | lockout window active | wait out the window | `401` generic message |
| Too many attempts | limiter rejects | wait for the window | `429` |
| Expired/invalid token | token validation fails | log in again | `401` |
| Stolen refresh token | reused (already-rotated) token presented | family revoked, everyone must log in again | `401`, session ends |
| Database or Redis down | dependency throws at startup or on use | restart once the dependency is back | generic `5xx`, never internals |

## Testing

**Covered** (integration tests against real Postgres and Redis containers):
register then login then call a protected route; weak password rejected; wrong
password returns 401; five failed logins lock the account; `/auth/me` without a token
returns 401; login returns 429 once the window is exhausted; login sets an HttpOnly,
Secure, SameSite=Strict cookie; refresh rotates the token and returns a new access
token; reusing a rotated token revokes the whole family; refresh without a cookie
returns 401; logout revokes the token.

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

### Rotating refresh tokens with family revocation

**Chose:** opaque hashed refresh tokens that rotate on every use, with reuse of an
old token revoking the entire family.
**Over:** long-lived non-rotating refresh tokens, or a stateless refresh JWT.
**Because:** rotation plus reuse detection turns token theft into a self-limiting
event: the moment either party uses a rotated token, the session dies. A hashed
opaque token also means a database leak yields nothing usable.
**Trade-off accepted:** refresh requires a database round trip (it is stateful), so
it cannot be validated purely from the token.

## Known gaps (next slices)

- **Exponential backoff.** Lockout and rate limiting are in place; per-attempt
  increasing delay is not yet implemented.
- **Real client IP behind the CDN.** The limiter uses the socket IP. Trusting
  forwarded headers from CloudFront is wired with the deployment config, not here.
