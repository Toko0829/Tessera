# Module Index

One line per module. Every module in the system appears here, or it does not
exist as far as this project is concerned.

Creating a module without `docs/modules/<module>.md` is incomplete work. See
§4 of [CLAUDE.md](../../CLAUDE.md) for the required sections.

| Module | Location | Purpose | Doc |
| --- | --- | --- | --- |
| persistence | `libs/persistence` | PostgreSQL access, EF Core context, Identity user store | [persistence.md](./persistence.md) |

---

## Adding a module

1. Copy [`_template.md`](./_template.md) to `docs/modules/<module>.md`
2. Fill in every section. "TBD" in a shipped module doc is a defect
3. Add the row to the table above
4. Commit the doc **in the same commit** as the module's first code

## Naming

Lower-kebab-case, matching the directory name in the codebase. `video-upload`,
not `VideoUpload` or `video_upload`.
