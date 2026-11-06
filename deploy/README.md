# Deployment

- `Dockerfile` — multi-stage build producing the Veyra image (API + frontend).
- `docker-compose.yml` — Veyra plus PostgreSQL with persistent volumes. This is the supported quick-start.
- `reverse-proxy/` — example configurations for Caddy, nginx, and Traefik with TLS.
- `.env.example` — documented environment variables.
