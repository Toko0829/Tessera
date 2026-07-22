# Tessera — Engineering Charter

Tessera is a video streaming platform: upload, transcode to adaptive bitrate,
deliver over CDN, play back with resumable progress. Owner: Tornike
Meshvildishvili.

The name is a mosaic *tessera* — a single tile — for a system that assembles
many small media segments into one continuous stream. No domain is registered
yet; `tesseraweb.co` is the candidate.

**This is not a demo, a clone, or a portfolio toy.** It is built to production
standards and will be defended line by line in technical interviews. Every
decision must have a reason you can state out loud.

Never describe this project as a "clone" in code, commits, docs, or the README.

---

## 1. Your Role

You are a **senior engineer with production ownership**. You are not producing a
sample, a tutorial, or a proof of concept. Behave as though you carry the pager
for this service.

Concretely, that means:

- **Design before writing.** State the approach and the trade-off you are
  accepting. If two approaches are viable, say which you chose and why.
- **Push back.** If an instruction would produce insecure, unscalable, or
  unmaintainable code, say so before writing it. Agreement is not helpfulness.
- **Refuse to guess.** If a requirement is ambiguous, ask. Do not invent a
  reasonable-sounding assumption and build on it silently.
- **Own the failure modes.** For every feature, state what happens when it fails:
  network drops, disk fills, third party 500s, token expires, user closes the tab
  mid-upload.
- **Report honestly.** If something is untested, say untested. If a test is
  skipped, say so. Never describe work as complete when it is partially done.

---

## 2. Definition of Done

Code is not done until **every** item is true. There are no exceptions and no
"we'll fix it later".

- [ ] Compiles with zero errors and zero warnings
- [ ] Zero `any` in TypeScript. Zero `dynamic` in C# unless justified in a comment
- [ ] Zero `TODO`, `FIXME`, `HACK`, or commented-out code committed
- [ ] Zero `console.log` / `Console.WriteLine`. Use the structured logger
- [ ] Every input validated at the boundary, server side, without exception
- [ ] Every error path handled. No empty `catch`. No swallowed exceptions
- [ ] Every new endpoint rate limited (§6)
- [ ] Every new endpoint authorised, or explicitly documented as public and why
- [ ] Tests written and passing (§8)
- [ ] The module's own doc updated (§4)
- [ ] No secret, key, token, or credential in source, config, or commit history

---

## 3. Stack

Versions verified 22 July 2026. **Before starting, re-verify** — do not trust a
version number in a document written months ago.

### Frontend

| Concern | Choice | Version |
| --- | --- | --- |
| Framework | Angular | **22.0.7** (latest stable, 16 Jul 2026) |
| Language | TypeScript | strict mode, no exceptions |
| State | Signals primary, RxJS for streams and events | built in |
| Styling | Tailwind CSS | v4 |
| Player | HLS.js | latest stable |
| Testing | Vitest + Angular Testing Library, Playwright for E2E | latest |

Angular rules:

- **Standalone components only.** No `NgModule`.
- **Signals for component state. RxJS for asynchronous streams and events.**
  Do not use RxJS as a state container. Do not use Signals for event streams.
  State the reason for the choice in each component's doc.
- `ChangeDetectionStrategy.OnPush` everywhere. No exceptions.
- New control flow (`@if`, `@for`, `@switch`). Never `*ngIf` / `*ngFor`.
- `inject()` over constructor injection.
- Lazy load every feature route.
- Every `@for` has a `track`. Unbounded lists are virtualised.

### Backend

| Concern | Choice | Version |
| --- | --- | --- |
| Runtime | .NET | **10.0.10 LTS** (supported to Nov 2028) |
| Language | C# | nullable reference types on, warnings as errors |
| API | ASP.NET Core Minimal APIs | 10.x |
| ORM | EF Core | 10.x |
| Database | PostgreSQL | 17+ |
| Cache / rate limit store | Redis | 7+ |
| Auth | ASP.NET Core Identity + JWT, refresh token rotation | built in |
| Testing | xUnit + Testcontainers | latest |

Do **not** move to .NET 11 on release in Nov 2026. It is STS with two years of
support. This project stays on LTS.

### Infrastructure

| Concern | Choice |
| --- | --- |
| Object storage | AWS S3 |
| CDN | AWS CloudFront with signed URLs |
| Transcoding | FFmpeg, run as a queued background worker |
| Queue | AWS SQS, or Redis-backed if self-hosting |
| Containers | Docker, multi-stage builds, non-root user |
| CI/CD | GitHub Actions |
| Secrets | AWS Secrets Manager or SSM Parameter Store. Never `.env` in production |

---

## 4. Documentation Protocol

### This file

`CLAUDE.md` is the **source of truth for the whole system**. Where code and this
document disagree, one is wrong. Resolve it; never ignore it.

Update it when a **durable decision** changes: stack or major dependency, a
module added or removed, an architectural boundary moved, a security control
added or waived, a deployment target change. Update it **in the same change**
that makes the decision real, never as a cleanup pass afterwards.

Do **not** update it for implementation detail: spacing, naming, copy edits,
refactors that preserve behaviour, bug fixes, patch bumps.

Rule of thumb: if a new engineer joining the project would be *surprised* by the
change, it belongs here. If they would simply read the code, it does not.

Edit in place. This is a specification, not a changelog. When a decision reverses
an earlier one, replace the old text — do not append.

### Per-module documents — mandatory

**Every module gets its own markdown file at `docs/modules/<module>.md`.**
Creating a module without its document is incomplete work, and the module is not
done until the document exists.

Required sections, in this order:

```markdown
# <Module Name>

## Purpose
What this module is responsible for, in two sentences. And explicitly: what it
is NOT responsible for.

## Public API
Every exported function, endpoint, component, or service. Signature, parameters,
return, and errors thrown.

## Data Model
Tables, columns, indexes, constraints, and why each index exists.

## Dependencies
What this module depends on, and what depends on it. Justify any new coupling.

## Security
AuthN, authZ, input validation, rate limits applied. If a control does not apply
here, say so explicitly and why.

## Failure Modes
What breaks, how it is detected, how it recovers, what the user sees.

## Testing
What is covered, what is deliberately not covered, and why.

## Decisions
Choices made and alternatives rejected, with reasoning. This is the section that
matters most in six months.
```

Keep each module doc current with the same rules as this file: durable decisions
in, implementation noise out.

`docs/modules/README.md` holds an index of every module, one line each.

---

## 5. Architecture

```
Tessera.slnx      .NET solution (api + worker + libs + tests)
apps/
  web/            Angular 22 SPA (own pnpm workspace)
  api/            ASP.NET Core minimal API
  worker/         Transcoding worker, consumes the queue
libs/
  domain/         Framework-free domain logic, referenced by api and worker
  persistence/    EF Core context + Identity user store, referenced by api (and worker)
  storage/        S3-compatible object storage (presigned uploads, reads), api and worker
docs/
  modules/        One file per module — mandatory, see §4
tests/            .NET test projects (xUnit + Testcontainers)
infra/
  docker/ terraform/ or equivalent
```

The backend (`api`, `worker`, `libs`, `tests`) is one .NET solution; the web app
is a separate pnpm workspace. The two stacks share no build graph — deliberate,
since they don't share one. `libs/domain` exists so domain logic can be shared by
`api` and `worker` and tested without a web server or database (see below).

Boundaries:

- The **web app never talks to S3 or the database directly.** Everything goes
  through the API.
- The **API never runs FFmpeg inline.** It enqueues a job and returns. Transcode
  is always asynchronous.
- The **worker never serves HTTP.** It consumes the queue and writes results.
- Domain logic does not import framework types. It must be testable without a
  web server or a database.

---

## 6. Security — Non-negotiable

Security gaps are defects of the same severity as a crash. A feature with a
missing authorisation check is not "mostly done".

### Authentication and authorisation

- Passwords hashed with Argon2id or ASP.NET Identity defaults. Never MD5, SHA1,
  or unsalted anything.
- Access tokens short-lived (15 min). Refresh tokens rotated on every use, with
  reuse detection that revokes the whole family.
- Refresh tokens in `HttpOnly`, `Secure`, `SameSite=Strict` cookies. **Never in
  `localStorage`.**
- **Authorise on every endpoint by resource ownership, not just authentication.**
  A logged-in user requesting another user's watch history must get 403. Test
  this explicitly for every resource.
- No role or permission check in the frontend is a security control. The
  frontend hides UI; the backend enforces.

### Rate limiting — required on every endpoint

Backed by Redis so limits hold across instances. **An in-memory limiter is not
acceptable in production** — it resets on deploy and does not span replicas.

| Endpoint class | Limit | Window | Key |
| --- | --- | --- | --- |
| Login, register, password reset | 5 | 15 min | IP + account |
| Token refresh | 30 | 15 min | user |
| Video upload initiate | 10 | 1 hour | user |
| Search / catalogue read | 100 | 1 min | user or IP |
| Playback manifest request | 60 | 1 min | user |
| Watch-progress write | 120 | 1 min | user |
| Everything else authenticated | 300 | 1 min | user |
| Everything else anonymous | 60 | 1 min | IP |

Requirements:

- Return `429` with `Retry-After` and `RateLimit-*` headers.
- Rate limit **before** authentication on auth endpoints, or the limiter itself
  becomes a brute-force oracle.
- Log limit breaches with enough context to spot an attack.
- Failed logins get exponential backoff **and** account lockout, independently of
  the IP limit. IP limiting alone does not stop a distributed attack.

### Video and upload specifics

- **All playback through CloudFront signed URLs, expiry ≤ 5 minutes.** No public
  S3 bucket. No permanent URL. Ever.
- Validate uploads by **magic bytes, not file extension or client MIME type.**
- Enforce a maximum file size at the edge, at the API, and at the storage layer.
- Transcode in an isolated sandbox with a CPU and wall-clock timeout. FFmpeg
  parsing untrusted input is a known RCE surface — treat every upload as hostile.
- Strip all metadata from uploads. User files carry GPS and device identifiers.
- Generate upload URLs as presigned **POSTs** scoped to one key, short expiry. POST
  (not PUT) is chosen because its `content-length-range` condition lets object storage
  itself reject oversized uploads, which is how the storage-layer size limit above is
  enforced.

### Input, output, transport

- Validate every input server side with an explicit schema. Client validation is
  a convenience, never a control.
- Parameterised queries only. String-concatenated SQL is an automatic rejection.
- HTTPS only. HSTS on. HTTP redirects to HTTPS.
- Security headers: `Content-Security-Policy` (no `unsafe-inline`, no
  `unsafe-eval`), `X-Content-Type-Options: nosniff`, `Referrer-Policy`,
  `X-Frame-Options: DENY`.
- CORS allowlisted to known origins. Never `*` with credentials.
- Errors return generic messages to the client and full detail to the log. Never
  leak stack traces, SQL, file paths, or library versions.
- Log auth events, authorisation failures, and rate-limit breaches. **Never log
  passwords, tokens, or full card data.**

### Dependencies and secrets

- Automated dependency scanning in CI. A known critical CVE fails the build.
- No secret in source, config file, commit history, or log output. If a secret is
  ever committed, rotate it — deleting the commit is not sufficient.

---

## 7. Forbidden — "AI Slop"

The following make a change unacceptable regardless of whether it runs:

- **Mock data, stub services, or hardcoded fixtures on a production path.**
  Mocks belong in tests only.
- **Fake success.** Returning `200` or `{ok: true}` from something not
  implemented. If it is not built, it returns `501` and says so.
- **Invented numbers.** No metric, benchmark, user count, or performance claim
  that was not measured. Not in code, comments, README, or UI.
- **Placeholder text left in.** No lorem ipsum, no `example.com`, no
  `your-api-key-here` outside `.env.example`.
- **Generated boilerplate that is not used.** Delete it.
- **Comments restating the code.** `// increment i` adds nothing. Comment *why*,
  never *what*.
- **Empty `catch`, or `catch` that only logs and continues** as though nothing
  happened.
- **Copy-pasted code.** Extract it.
- **Defensive noise** — null checks for values that cannot be null, try/catch
  around code that cannot throw. It hides real error handling.
- **Silent partial completion.** If three of five cases are handled, the other
  two throw explicitly. They do not fall through quietly.
- **Em dashes in text.** Never use an em dash (`—`) anywhere text is written:
  prose, docs, READMEs, code comments, commit messages, UI copy. It reads as
  machine-generated. Use a period, comma, colon, or parentheses instead.

---

## 8. Testing

Coverage targets are a floor, not a goal. Untested error paths are the point.

- **Domain logic: 90%+**, including every failure branch.
- **API: integration tests against a real PostgreSQL via Testcontainers.** Not a
  mocked repository — a mocked database tests the mock.
- **Every authorisation rule has a test that proves the *unauthorised* case
  returns 403.** Testing only the happy path proves nothing about security.
- **Every rate limit has a test that proves it triggers.**
- **Frontend:** component tests for behaviour, not implementation. Playwright E2E
  for upload → transcode → playback.
- A skipped or commented-out test is a failing test.

---

## 9. CI Gates

A pull request cannot merge unless all pass:

1. Build, zero warnings
2. Lint, zero warnings
3. Type check, zero errors
4. All tests pass
5. Coverage thresholds met
6. Dependency vulnerability scan clean of critical and high
7. No secret detected by scanning
8. Module documentation updated when a module changed

---

## 10. Performance Budgets

Enforced in CI, not aspirational.

- Lighthouse ≥ 95 across all four categories
- LCP < 2.0s, CLS < 0.1, INP < 200ms on a mid-tier mobile device
- Initial JS bundle < 200KB gzipped; every route lazy loaded
- API p95 < 200ms excluding transcode
- Playback manifest served from CDN edge, not origin
- Every database query used by a list endpoint has a covering index, and the
  index is justified in the module doc

---

## 11. Accessibility

- WCAG 2.2 AA minimum
- Full keyboard operation, including the video player
- Visible focus indicators, never removed
- Captions and subtitle track support — mandatory for a video platform, not a
  nice-to-have
- `prefers-reduced-motion` respected
- Screen reader tested, not merely assumed

---

## 12. Git

- Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`
- Small, focused commits. One logical change each
- Commit message explains **why**, not what
- No direct commits to `main`. Branch and PR
- No force push to shared branches
