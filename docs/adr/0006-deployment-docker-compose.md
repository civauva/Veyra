# 0006. Deployment: Docker Compose with Veyra and PostgreSQL

Date: 2026-11-07
Status: Accepted

## Context

The README's original principle was a single-container deployment. ADR 0003 chose PostgreSQL, which is a separate
process with its own lifecycle, upgrades, and backups.

## Decision

The supported quick-start is a `docker-compose.yml` in `deploy/` with two services: `veyra` and `postgres`, each with a
persistent volume. The Veyra image contains the API and the built frontend. Pointing Veyra at an existing PostgreSQL
server is supported through configuration.

## Alternatives considered

- Single image with embedded PostgreSQL: one container, but couples database upgrades to application releases and
  makes backups non-standard.
- Veyra image only, bring your own PostgreSQL: supported as a configuration, but a poor first-run experience.
- Kubernetes/Helm first: a later addition once the compose deployment is stable.

## Consequences

- "Simple by default" now means one compose file, not one container; the README wording changes accordingly.
- Backups and upgrades follow standard PostgreSQL practice and are documented in `docs/guides/deployment.md`.
- Reverse proxy and TLS examples sit alongside the compose file in `deploy/reverse-proxy/`.
