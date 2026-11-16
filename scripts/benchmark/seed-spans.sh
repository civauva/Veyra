#!/usr/bin/env bash
# Benchmark (ADR 0014): fills the trace tables of a benchmark installation with a week of traces at the reference
# scale, 500,000 traces of ten spans a day, in time order. Never run it against an installation that holds real data:
# it empties storage.span first. Seed logs and metrics with seed.sh.
#
# POSTGRES_CONTAINER, TRACES_PER_DAY, TRACE_DAYS, and PARALLEL can be set in the environment.
set -euo pipefail
cd "$(dirname "$0")"

POSTGRES_CONTAINER=${POSTGRES_CONTAINER:-veyra-bench-postgres-1}
TRACES_PER_DAY=${TRACES_PER_DAY:-500000}
TRACE_DAYS=${TRACE_DAYS:-7}
PARALLEL=${PARALLEL:-4}

NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
TODAY=$(date -u +%Y-%m-%d)
export POSTGRES_CONTAINER TRACES_PER_DAY NOW TODAY

psql() { docker exec -i "$POSTGRES_CONTAINER" psql -q -U veyra -d veyra -v ON_ERROR_STOP=1 "$@"; }

psql -c "TRUNCATE storage.span"

# Every day's partition first, and tomorrow's: the last traces of a day have spans just after midnight, which must
# find their day's partition, not the default one, while the days are filled side by side.
for offset in $(seq $(( TRACE_DAYS - 1 )) -1 -1); do
  day=$(date -u -d "$TODAY - $offset days" +%Y-%m-%d)
  psql -c "CREATE TABLE IF NOT EXISTS storage.span_$(date -u -d "$day" +%Y%m%d) PARTITION OF storage.span
            FOR VALUES FROM ('$day 00:00:00+00') TO ('$(date -u -d "$day + 1 day" +%Y-%m-%d) 00:00:00+00')"
done

echo "traces: $TRACE_DAYS days at $TRACES_PER_DAY traces of ten spans a day"
seq $(( TRACE_DAYS - 1 )) -1 0 | xargs -P "$PARALLEL" -I{} bash -c '
  set -euo pipefail
  day=$(date -u -d "$TODAY - {} days" +%Y-%m-%d)
  docker exec -i "$POSTGRES_CONTAINER" psql -q -U veyra -d veyra -v ON_ERROR_STOP=1 -v day="$day" \
    -v n="$TRACES_PER_DAY" -v until="$NOW" < seed-spans.sql
  echo "  $day done"
'

psql -c "VACUUM ANALYZE storage.span"
echo "done"
