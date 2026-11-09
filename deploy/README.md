# Deployment

Everything needed to run Veyra with Docker Compose. The step-by-step guide is
[`docs/guides/deployment.md`](../docs/guides/deployment.md).

| File | Purpose |
| --- | --- |
| `Dockerfile` | Multi-stage build of the Veyra image: the API now, the frontend once it exists. |
| `docker-compose.yml` | Veyra plus PostgreSQL 18 with a persistent volume. The supported quick-start (ADR 0006). |
| `docker-compose.caddy.yml` | Adds Caddy in front of Veyra for a public server with automatic HTTPS. |
| `reverse-proxy/caddy/Caddyfile` | Caddy configuration. nginx and Traefik examples may follow. |
| `.env.example` | Documented settings. Copy to `deploy/.env`, which git ignores, and fill it in. |
