#!/usr/bin/env bash
# Checks integrations/postgres/collector.yaml against a real PostgreSQL: starts one, seeds it, causes a deadlock, holds
# a lock so a session waits on it, runs a long query, runs the Collector for 90 seconds, and checks what it sent
# (check.mjs).
#
# Needs Docker, OpenSSL, and Node.js. Run from anywhere: bash integrations/postgres/test/run.sh
# POSTGRES_IMAGE picks the PostgreSQL version, for example postgres:14 or postgres:18-alpine.
# POSTGRES_TLS=on (the default) gives the server a self-signed certificate, which the Collector trusts without
# checking; off reads it without TLS.
set -euo pipefail

cd "$(dirname "$0")"

# Throwaway passwords for this run only.
ADMIN_PASSWORD="Admin$(openssl rand -hex 16)"
MONITOR_PASSWORD="Mon$(openssl rand -hex 16)"
export ADMIN_PASSWORD MONITOR_PASSWORD

compose() {
  docker compose --file compose.yml --profile collector "$@"
}

psql() {
  # MSYS_NO_PATHCONV keeps Git Bash on Windows from rewriting arguments that look like paths.
  MSYS_NO_PATHCONV=1 compose exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 -q "$@"
}

cleanup() {
  compose down --volumes --remove-orphans > /dev/null 2>&1 || true
}
trap cleanup EXIT

rm -rf out
mkdir -p out/tls
: > out/telemetry.jsonl

if [ "${POSTGRES_TLS:-on}" = "on" ]; then
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=postgres" \
    -keyout out/tls/server.key -out out/tls/server.crt 2> /dev/null
  export TLS_INSECURE=false TLS_SKIP_VERIFY=true SSLMODE=require
else
  export TLS_INSECURE=true TLS_SKIP_VERIFY=false SSLMODE=disable
fi

compose up --detach postgres

echo "Waiting for PostgreSQL..."
for _ in $(seq 1 60); do
  if psql -c "SELECT 1" > /dev/null 2>&1; then
    break
  fi
  sleep 2
done

psql -v monitor_password="$MONITOR_PASSWORD" < seed.sql > /dev/null

if [ "${POSTGRES_TLS:-on}" = "on" ]; then
  [ "$(psql -At -c "SHOW ssl")" = "on" ] || { echo "The server did not turn TLS on."; exit 1; }
fi

# One session holds a row; the next waits on it, so it is blocked for longer than 30 seconds while the Collector reads.
psql -d shop -c "BEGIN; UPDATE orders SET status = 'held' WHERE id = 1; SELECT pg_sleep(150); ROLLBACK;" \
  > /dev/null 2>&1 &
sleep 2
psql -d shop -c "UPDATE orders SET status = 'paid' WHERE id = 1;" > /dev/null 2>&1 &

# A long report in another database, which must be its longest query.
psql -d reporting -c "SELECT pg_sleep(150);" > /dev/null 2>&1 &

# Two transactions take rows 3 and 4 in opposite order, so PostgreSQL ends one as a deadlock victim.
psql -d shop -c "BEGIN; UPDATE orders SET status = 'a' WHERE id = 3; SELECT pg_sleep(3);
  UPDATE orders SET status = 'a' WHERE id = 4; COMMIT;" > /dev/null 2>&1 &
sleep 1
psql -d shop -c "BEGIN; UPDATE orders SET status = 'b' WHERE id = 4; SELECT pg_sleep(3);
  UPDATE orders SET status = 'b' WHERE id = 3; COMMIT;" > /dev/null 2>&1 || true

compose up --detach collector
echo "Collecting for 90 seconds..."
sleep 90

compose stop collector > /dev/null
# The Collector's whole log, to read when a check fails.
compose logs --no-color collector > out/collector.log 2>&1

# The receiver warns once at start that the feature gate receiver.postgresql.separateSchemaAttr is off. Turning it on
# would take a command-line flag on every agent; with it off, a table's name holds its schema, as public.orders. Every
# other error or warning fails the check.
problems=$(grep -P '\t(error|warn)\t' out/collector.log | grep -v 'separateSchemaAttr is not enabled' || true)

if [ -n "$problems" ]; then
  echo "The Collector logged errors or warnings:"
  echo "$problems" | head -20
  exit 1
fi

node check.mjs out/telemetry.jsonl --describe
