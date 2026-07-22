# Persistence

## Purpose

Owns the application's PostgreSQL database access: the EF Core context, the
Identity-backed user store, and the migration history. It is the single place the
schema is defined and evolved.

**Not responsible for:** business rules (those live in `libs/domain`, framework-free),
HTTP concerns, authentication flows, or issuing tokens. This layer stores and
retrieves data; it does not decide policy.

## Public API

| Name | Signature | Returns | Throws / Errors |
| --- | --- | --- | --- |
| `TesseraDbContext` | `TesseraDbContext(DbContextOptions<TesseraDbContext>)` | EF Core context over the Identity schema | EF Core exceptions on query/save |
| `TesseraUser` | `IdentityUser<Guid>` + `CreatedAt: DateTimeOffset` | — | — |
| `TesseraDbContextFactory` | `IDesignTimeDbContextFactory<TesseraDbContext>` | design-time context for `dotnet ef` | — |

No HTTP endpoints. This is a class library consumed by `apps/api` (and later
`apps/worker`).

## Data Model

ASP.NET Core Identity's standard schema, keyed by `Guid`:

`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserRoles`,
`AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`.

`AspNetUsers` carries one custom column, `CreatedAt` (`timestamptz`).

| Index | Columns | Serves which query | Why |
| --- | --- | --- | --- |
| `UserNameIndex` (unique) | `NormalizedUserName` | login/lookup by username | Identity enforces unique usernames via the normalized form |
| `EmailIndex` | `NormalizedEmail` | lookup by email | Identity looks users up by normalized email |
| `RoleNameIndex` (unique) | `NormalizedName` | role lookup | Identity enforces unique role names |

All created by Identity's model. No hand-rolled indexes yet; domain tables and
their indexes arrive with their own modules.

## Dependencies

**Depends on:** EF Core 10, Npgsql (PostgreSQL provider),
ASP.NET Core Identity EntityFrameworkCore.
**Depended on by:** `apps/api` (references the project; DI wiring and the runtime
connection string arrive with the auth endpoints in the next slice). `apps/worker`
will depend on it when transcode status writes land.

The coupling to Identity is deliberate: the charter mandates Identity + JWT, so the
user store is Identity's, not a bespoke table.

## Security

- **Authentication / authorisation:** none enforced here — this layer has no request
  context. Those controls live in the auth module that sits on top of this store.
- **Input validation:** not applicable at this layer; callers validate at the API
  boundary before persisting.
- **Rate limits:** not applicable — no endpoints.
- **Identifiers:** `Guid` primary keys so user IDs are non-sequential and cannot be
  enumerated.
- **Secrets:** no credentials in source. The design-time factory's connection string
  is a throwaway localhost value with no password and never opens a connection.
  Runtime connection strings come from configuration / AWS Secrets Manager (§6).

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| Database unreachable | Npgsql throws on connect | caller/API surfaces a 5xx; ret/health check fails | generic error, never DB details |
| Migration not applied | schema-mismatch errors at query time | run `dotnet ef database update` / apply on deploy | nothing if applied in the deploy pipeline |
| Duplicate username/email | unique-index violation on save | caller maps to a validation error | "that name/email is taken" (added with the register endpoint) |
| Concurrent update | EF concurrency exception (once tokens/rows use it) | caller retries or rejects | handled at the endpoint |

## Testing

**Covered:** an integration test (`TesseraDbContextTests`) that starts a real
PostgreSQL 17 in a throwaway Testcontainers container, applies the migration, and
round-trips a `TesseraUser` — proving the schema builds and persists against real
Postgres, not a mock (§8).

**Deliberately not covered:** Identity's own behaviour (password hashing, token
generation) — that is the framework's responsibility and is exercised through the
auth module's tests once endpoints exist. No authorisation tests here because this
layer enforces no authorisation.

## Decisions

### Shared library, not inside the API

**Chose:** a standalone `libs/persistence` project.
**Over:** putting the `DbContext` inside `apps/api`.
**Because:** the worker will also read and write the database (transcode status), so
the store must be shareable by both apps, mirroring `libs/domain`.
**Trade-off accepted:** one more project to build and reference.

### Guid keys

**Chose:** `IdentityUser<Guid>`.
**Over:** the default `string`/int identifiers.
**Because:** non-sequential IDs can't be guessed or enumerated, which matters for a
resource-ownership authorisation model (§6).
**Trade-off accepted:** Guids are wider than ints and slightly less index-friendly —
negligible at this scale.

### Identity's default password hashing

**Chose:** ASP.NET Core Identity defaults (PBKDF2, high iteration count).
**Over:** pulling in Argon2id now.
**Because:** the charter permits Identity defaults, and they are well-audited; adding
Argon2id is an isolated future change if we decide the extra cost is worth it.
**Trade-off accepted:** not the theoretically strongest KDF, but a safe, standard one.
