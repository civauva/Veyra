#!/usr/bin/env bash
# Benchmark (ADR 0015): fills a benchmark installation with the reference volume, in time order as ingestion writes it:
# LOG_DAYS days of logs at PER_DAY records a day and METRIC_DAYS days of metric points, today up to now. Everything it
# writes is handed to Veyra's backfill, exactly as after an upgrade, so the counts and rollups are Veyra's own.
#
# Veyra is stopped while seeding, so no record is counted twice, and started again at the end. Run from anywhere:
#   scripts/benchmark/seed.sh
# Settings come from the environment; the defaults are the reference volume.
set -euo pipefail
cd "$(dirname "$0")"

POSTGRES_CONTAINER=${POSTGRES_CONTAINER:-veyra-bench-postgres-1}
VEYRA_CONTAINER=${VEYRA_CONTAINER:-veyra-bench-veyra-1}
PER_DAY=${PER_DAY:-3000000}
LOG_DAYS=${LOG_DAYS:-14}
METRIC_DAYS=${METRIC_DAYS:-30}
PARALLEL=${PARALLEL:-8}

NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
TODAY=$(date -u +%Y-%m-%d)
SECONDS_TODAY=$(( $(date -u +%s) - $(date -u -d "$TODAY" +%s) ))
START=$(date -u -d "$TODAY - $(( METRIC_DAYS - 1 )) days" +%Y-%m-%dT00:00:00Z)
export POSTGRES_CONTAINER PER_DAY NOW TODAY SECONDS_TODAY START

psql() { docker exec -i "$POSTGRES_CONTAINER" psql -q -U veyra -d veyra -v ON_ERROR_STOP=1 "$@"; }

echo "stopping $VEYRA_CONTAINER"
docker stop "$VEYRA_CONTAINER" > /dev/null

psql -c "TRUNCATE storage.log_record, storage.log_count_minute, storage.log_count_hour, storage.log_count_backfill,
         storage.metric_point, storage.metric_rollup_5m, storage.metric_rollup_1h, storage.metric_rollup_dirty,
         storage.metric_rollup_backfill"

echo "logs: $LOG_DAYS days at $PER_DAY records a day"
seq $(( LOG_DAYS - 1 )) -1 0 | xargs -P "$PARALLEL" -I{} bash -c '
  day=$(date -u -d "$TODAY - {} days" +%Y-%m-%d)
  n=$PER_DAY
  [ {} -eq 0 ] && n=$(( PER_DAY * SECONDS_TODAY / 86400 ))
  docker exec -i "$POSTGRES_CONTAINER" psql -q -U veyra -d veyra -v ON_ERROR_STOP=1 -v day="$day" -v n="$n" \
    -v until="$NOW" < seed-logs.sql
'

echo "metrics: $METRIC_DAYS days, one point a minute for every series"
psql < seed-metric-series.sql
seq $(( METRIC_DAYS - 1 )) -1 0 | xargs -P "$PARALLEL" -I{} bash -c '
  day=$(date -u -d "$TODAY - {} days" +%Y-%m-%d)
  docker exec -i "$POSTGRES_CONTAINER" psql -q -U veyra -d veyra -v ON_ERROR_STOP=1 -v day="$day" -v until="$NOW" \
    -v start="$START" < seed-metric-points.sql
'

# As after an upgrade: every seeded log day waits for its counts, every metric day before this hour for its rollups,
# and the rollup job takes over at this hour.
psql <<'SQL'
INSERT INTO storage.log_count_backfill (day, up_to_id)
SELECT DISTINCT (timestamp AT TIME ZONE 'UTC')::date, (SELECT max(id) FROM storage.log_record)
FROM storage.log_record;

UPDATE storage.metric_rollup_state
SET five_minutes = date_bin('1 hour', now(), timestamptz '1970-01-01 00:00:00+00'),
    hours = date_bin('1 hour', now(), timestamptz '1970-01-01 00:00:00+00');

INSERT INTO storage.metric_rollup_backfill (day, up_to)
SELECT DISTINCT (timestamp AT TIME ZONE 'UTC')::date, (SELECT five_minutes FROM storage.metric_rollup_state)
FROM storage.metric_point
WHERE timestamp < (SELECT five_minutes FROM storage.metric_rollup_state);

UPDATE storage.metric_series SET last_seen = now();
SQL

psql -c "VACUUM ANALYZE storage.log_record"
psql -c "VACUUM ANALYZE storage.metric_point"
psql -Atc "SELECT 'logs: ' || count(*) || ' records' FROM storage.log_record"
psql -Atc "SELECT 'metrics: ' || count(*) || ' points' FROM storage.metric_point"

echo "starting $VEYRA_CONTAINER; it counts and rolls up the seeded days in the background, newest first"
docker start "$VEYRA_CONTAINER" > /dev/null
