#!/usr/bin/env bash
# Checks integrations/sqlserver/collector.yaml against a real SQL Server: starts one with SQL Agent, seeds it, holds a
# lock so a session is blocked, runs the Collector for 90 seconds, and checks what it sent (check.mjs).
#
# Needs Docker and Node.js. Run from anywhere: bash integrations/sqlserver/test/run.sh
# MSSQL_IMAGE_TAG picks the SQL Server version, for example 2019-latest or 2025-latest.
set -euo pipefail

cd "$(dirname "$0")"

# Throwaway passwords for this run only.
SA_PASSWORD="Sa-$(openssl rand -hex 16)"
MONITOR_PASSWORD="Mon-$(openssl rand -hex 16)"
export SA_PASSWORD MONITOR_PASSWORD

compose() {
  docker compose --file compose.yml --profile collector "$@"
}

sqlcmd() {
  # MSYS_NO_PATHCONV keeps Git Bash on Windows from rewriting the tool's path.
  MSYS_NO_PATHCONV=1 compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SA_PASSWORD" "$@"
}

cleanup() {
  compose down --volumes --remove-orphans > /dev/null 2>&1 || true
}
trap cleanup EXIT

mkdir -p out
: > out/telemetry.jsonl

compose up --detach mssql

echo "Waiting for SQL Server..."
for _ in $(seq 1 60); do
  if sqlcmd -Q "SELECT 1" > /dev/null 2>&1; then
    break
  fi
  sleep 2
done

sqlcmd -v MONITOR_PASSWORD="$MONITOR_PASSWORD" -i /dev/stdin < seed.sql > /dev/null

# One session holds a row; the next waits on it, so it is both blocked and the longest running query.
sqlcmd -d shop -Q "BEGIN TRAN; UPDATE dbo.Orders SET Status = N'held' WHERE Id = 1; WAITFOR DELAY '00:03:00'; ROLLBACK;" > /dev/null 2>&1 &
sleep 2
sqlcmd -d shop -Q "UPDATE dbo.Orders SET Status = N'paid' WHERE Id = 1;" > /dev/null 2>&1 &

# The usual head of a blocking chain: a connection that changed a row, then went quiet without committing. It sleeps
# with its transaction open, holding the row, while another update waits for it (ADR 0020, the live view).
(printf "BEGIN TRAN;
UPDATE dbo.Orders SET Status = N'held' WHERE Id = 2;
GO
"; sleep 150; printf "ROLLBACK;
GO
")   | MSYS_NO_PATHCONV=1 compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -d shop   > /dev/null 2>&1 &
sleep 2
sqlcmd -d shop -Q "UPDATE dbo.Orders SET Status = N'paid' WHERE Id = 2;" > /dev/null 2>&1 &

compose up --detach collector
echo "Collecting for 90 seconds..."

# A query run once, as people run them in SQL Server Management Studio, after the agent's first read: its statement
# must still arrive, counted in full (VEY-43).
sleep 30
sqlcmd -d shop -Q "SELECT COUNT_BIG(*) AS pairs FROM sys.all_columns AS a CROSS JOIN (SELECT TOP (2000) object_id FROM sys.all_columns) AS b WHERE a.object_id % 7 <> b.object_id % 5" > /dev/null
sleep 60
compose stop collector > /dev/null

if compose logs collector 2>&1 | grep -q '"error"'; then
  echo "The Collector logged errors:"
  compose logs collector 2>&1 | grep '"error"' | head -20
  exit 1
fi

node check.mjs out/telemetry.jsonl
