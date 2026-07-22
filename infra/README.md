# infra

Deployment and infrastructure for Tessera: Docker images (multi-stage,
non-root), infrastructure-as-code, and CI/CD wiring.

Nothing here yet. Each piece lands with the app it supports — an app is not
"done" without the container that ships it (CLAUDE.md §3, §5).

Secrets never live here. Production configuration is resolved from AWS Secrets
Manager / SSM at runtime, never from a committed file (CLAUDE.md §6).
