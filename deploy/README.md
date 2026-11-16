# Deployment

Everything needed to run Veyra with Docker Compose. The step-by-step guide is
[`docs/guides/deployment.md`](../docs/guides/deployment.md).

| File | Purpose |
| --- | --- |
| `Dockerfile` | Multi-stage build of the Veyra image: the API now, the frontend once it exists. |
| `docker-compose.yml` | Veyra, its built-in agent, and PostgreSQL 18 with persistent volumes. The supported quick-start (ADR 0006). |
| `agent/Dockerfile` | The built-in agent: the official OpenTelemetry Collector and OpAMP supervisor, unchanged (ADR 0019). |
| `docker-compose.caddy.yml` | Adds Caddy in front of Veyra for a public server with automatic HTTPS. |
| `docker-compose.edge.yml` | For a server whose shared proxy serves several apps: no published ports, memory limits. |
| `reverse-proxy/caddy/Caddyfile` | Caddy configuration. nginx and Traefik examples may follow. |
| `.env.example` | Documented settings. Copy to `deploy/.env`, which git ignores, and fill it in. |
