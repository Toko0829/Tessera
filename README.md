# Tessera

A video streaming platform: upload, transcode to adaptive bitrate, deliver over a
CDN, and play back with resumable progress. The name is a mosaic *tessera* — a
single tile — for a system that assembles many small media segments into one
continuous stream.

This is a production-standards build, not a demo or a clone. The engineering
rules that govern it live in [`CLAUDE.md`](./CLAUDE.md); read that first.

## Repository layout

```
apps/
  web/       Angular 22 SPA — the only client; never talks to S3 or the DB directly
  api/       ASP.NET Core 10 minimal API — enqueues transcode jobs, never runs FFmpeg inline
  worker/    Transcoding worker — consumes the queue, runs FFmpeg, never serves HTTP
docs/
  modules/   One document per module (mandatory — see CLAUDE.md §4)
infra/       Docker, IaC, and deployment configuration
```

The boundaries above are load-bearing, not stylistic. See `CLAUDE.md` §5.

## Toolchain

| Area | Tool | Version |
| --- | --- | --- |
| Web | Node / Angular | 22.x / 22.x |
| Web packages | pnpm | 10.x |
| Backend | .NET SDK | 10.x LTS |
| Datastore | PostgreSQL / Redis | 17+ / 7+ |
| Containers | Docker | 29.x |

Backend and frontend are separate workspaces: pnpm manages `apps/web`, a .NET
solution manages `apps/api` and `apps/worker`. There is no shared build graph
because the two stacks do not share one — this is deliberate (see the scaffold
decision in the module docs).

## Getting started

> Status: repository scaffold in progress. Commands are filled in as each app
> lands, so this section always reflects what actually exists — never aspirational
> steps.

### Web (`apps/web`)

```bash
pnpm install
pnpm --filter web start
```

### Backend (`apps/api`, `apps/worker`)

```bash
dotnet restore
dotnet run --project apps/api
```

## License

Not yet chosen. Do not assume this code is open source until a `LICENSE` file
exists.
