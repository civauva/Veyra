#!/usr/bin/env bash
# Deploys Veyra to a server that runs Docker behind a shared edge proxy (deploy/docker-compose.edge.yml), over SSH.
# Builds the images here from a clean checkout of a branch or tag, copies them to the server, and starts them there.
#
#   DEPLOY_HOST=veyra bash scripts/deploy.sh [ref]
#
# DEPLOY_HOST    an SSH host or alias that logs in without a prompt, as a user in the server's docker group
# DEPLOY_DIR     the folder on the server, /opt/veyra by default; it holds the compose files and the server's .env
# EDGE_SITES     the edge proxy's folder of site files, /opt/edge/sites by default; its veyra.caddy must be writable
# ref            what to deploy, main by default
#
# The server's .env holds its secrets and never leaves it. On the first run the script puts deploy/.env.example
# there and stops, so it can be filled in. Nothing in this script names a host or holds a secret.
set -euo pipefail

host="${DEPLOY_HOST:?Set DEPLOY_HOST to the SSH host or alias of the server}"
dir="${DEPLOY_DIR:-/opt/veyra}"
sites="${EDGE_SITES:-/opt/edge/sites}"
ref="${1:-main}"
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"

cleanup() {
  git -C "$root" worktree remove --force "$work/src" > /dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

remote() {
  ssh -o BatchMode=yes "$host" "$@"
}

commit="$(git -C "$root" rev-parse --short "$ref")"
echo "Deploying $ref ($commit) to $host:$dir"

if ! remote "test -f '$dir/.env'"; then
  scp -q -o BatchMode=yes "$root/deploy/.env.example" "$host:$dir/.env"
  remote "chmod 600 '$dir/.env'"
  echo "First run: $dir/.env is the template."
  echo "Fill it in on the server (ssh -t $host nano $dir/.env), then deploy again."
  exit 2
fi

domain="$(remote "grep -E '^VEYRA_DOMAIN=' '$dir/.env' | cut -d= -f2-" | tr -d '\r"')"

if [ -z "$domain" ]; then
  echo "Set VEYRA_DOMAIN in $dir/.env on the server, then deploy again."
  exit 2
fi

# A clean checkout, so nothing uncommitted on this machine is deployed.
git -C "$root" worktree add --detach --quiet "$work/src" "$ref"

echo "Building the images..."
docker build --quiet -f "$work/src/deploy/Dockerfile" -t veyra:local -t "veyra:$commit" "$work/src" > /dev/null
docker build --quiet -t veyra-agent:local -t "veyra-agent:$commit" "$work/src/deploy/agent" > /dev/null

echo "Copying them to the server..."
docker save veyra:local "veyra:$commit" veyra-agent:local "veyra-agent:$commit" \
  | gzip \
  | remote "gunzip | docker load" > /dev/null
scp -q -o BatchMode=yes "$work/src/deploy/docker-compose.yml" "$root/deploy/docker-compose.edge.yml" "$host:$dir/"

echo "Starting Veyra..."
compose="docker compose -p veyra -f docker-compose.yml -f docker-compose.edge.yml"
remote "cd '$dir' && $compose up -d --no-build --remove-orphans"

# The site: the app on veyra-web:8080, OTLP over HTTP on veyra-web:4318. Validated before the reload; an invalid file
# leaves the proxy on its old configuration.
site="# Veyra ($dir), written by scripts/deploy.sh.
$domain {
	@otlp path /v1/logs /v1/metrics /v1/traces
	reverse_proxy @otlp veyra-web:4318
	reverse_proxy veyra-web:8080
}"

if remote "test -w '$sites/veyra.caddy'"; then
  printf '%s\n' "$site" | remote "cat > '$sites/veyra.caddy'"
  caddy="docker exec edge-caddy-1 caddy"
  remote "$caddy validate --config /etc/caddy/Caddyfile > /dev/null && $caddy reload --config /etc/caddy/Caddyfile"
else
  echo "Cannot write $sites/veyra.caddy. As root on the server:"
  echo "  touch $sites/veyra.caddy; chown $(remote whoami) $sites/veyra.caddy"
  exit 3
fi

echo "Waiting for https://$domain ..."
for _ in $(seq 1 30); do
  if curl -fsS -o /dev/null "https://$domain/"; then
    echo "Veyra $commit runs at https://$domain"
    exit 0
  fi
  sleep 5
done

echo "https://$domain did not answer within 150 seconds. The containers:"
remote "cd '$dir' && docker compose -p veyra ps"
exit 1
