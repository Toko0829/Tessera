# Module Index

One line per module. Every module in the system appears here, or it does not
exist as far as this project is concerned.

Creating a module without `docs/modules/<module>.md` is incomplete work. See
§4 of [CLAUDE.md](../../CLAUDE.md) for the required sections.

| Module | Location | Purpose | Doc |
| --- | --- | --- | --- |
| persistence | `libs/persistence` | PostgreSQL access, EF Core context, Identity user store | [persistence.md](./persistence.md) |
| auth | `apps/api` (`Auth/`) | Register, login, JWT issuance, rate limiting, lockout | [auth.md](./auth.md) |
| web-auth | `apps/web` (`core/auth`, `features/auth`) | Login and signup UI, client auth state | [web-auth.md](./web-auth.md) |
| storage | `libs/storage` | S3-compatible object storage (presigned uploads, reads) | [storage.md](./storage.md) |
| video-upload | `apps/api` (`Videos/`) | Presigned upload, magic-byte validation, video records | [video-upload.md](./video-upload.md) |
| web-video | `apps/web` (`core/video`, `features/home`) | Upload UI and library, client upload flow | [web-video.md](./web-video.md) |
| transcode | `apps/worker`, `libs/queue` | Redis job queue, FFmpeg worker, HLS ladder | [transcode.md](./transcode.md) |
| playback | `apps/api` (`Playback/`) | Authorised HLS delivery: playlists via API, segments via signed redirects | [playback.md](./playback.md) |
| web-playback | `apps/web` (`features/watch`) | Player page, hls.js, honest unsupported/error states | [web-playback.md](./web-playback.md) |

---

## Adding a module

1. Copy [`_template.md`](./_template.md) to `docs/modules/<module>.md`
2. Fill in every section. "TBD" in a shipped module doc is a defect
3. Add the row to the table above
4. Commit the doc **in the same commit** as the module's first code

## Naming

Lower-kebab-case, matching the directory name in the codebase. `video-upload`,
not `VideoUpload` or `video_upload`.
