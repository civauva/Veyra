#!/usr/bin/env bash
# Checks integrations/docker/collector.yaml against real containers: one that logs, one that keeps restarting, and
# one that keeps a CPU busy. Runs the Collector beside them for 75 seconds and checks what it sent (check.mjs).
#
# Needs Docker on Linux, or Docker Desktop, whose Linux VM holds /var/lib/docker/containers, and Node.js. Run from
# anywhere: bash integrations/docker/test/run.sh
set -euo pipefail

cd "$(dirname "$0")"

compose() {
  docker compose --file compose.yml --profile collector "$@"
}

cleanup() {
  compose down --volumes --remove-orphans > /dev/null 2>&1 || true
}
trap cleanup EXIT

mkdir -p out
: > out/telemetry.jsonl

compose up --detach logger restarter busy
compose up --detach collector
echo "Collecting for 75 seconds..."
sleep 75

compose stop collector > /dev/null
# The Collector's whole log, to read when a check fails.
compose logs --no-color collector > out/collector.log 2>&1

# docker_stats now and then loses one container's read with "context canceled", mostly in its first read after start
# and for containers that were already running; that container misses one point. Every other error or warning fails
# the check, and check.mjs still needs every metric.
problems=$(grep -P '\t(error|warn)\t' out/collector.log | grep -v '"error": "context canceled' || true)

if [ -n "$problems" ]; then
  echo "The Collector logged errors or warnings:"
  echo "$problems" | head -20
  exit 1
fi

node check.mjs out/telemetry.jsonl
