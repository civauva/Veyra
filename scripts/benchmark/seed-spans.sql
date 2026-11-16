-- Benchmark (ADR 0014): one day of traces at the reference scale, in time order as ingestion writes them. psql
-- variables: day (YYYY-MM-DD, a UTC day), n (traces; ten spans each), until (an instant; traces stop there, so today
-- ends at now).
-- Each trace is a request to one of five services that checks a token with identity, pays through payments-gateway,
-- which calls psp-client and the database. Durations spread log-normally around 150 ms; 3 % of traces fail in
-- psp-client with a timeout exception, which their callers pass on; one in ten is in staging.
\set ON_ERROR_STOP on

SELECT format(
    'CREATE TABLE IF NOT EXISTS storage.span_%s PARTITION OF storage.span FOR VALUES FROM (%L) TO (%L)',
    to_char(:'day'::date, 'YYYYMMDD'),
    (:'day'::date)::timestamptz AT TIME ZONE 'UTC',
    (:'day'::date + 1)::timestamptz AT TIME ZONE 'UTC') \gexec

SET synchronous_commit = off;

WITH trace AS (
    SELECT
        g,
        :'day' || '/' || g AS key,
        decode(md5(:'day' || '/' || g || '/trace'), 'hex') AS trace_id,
        (:'day' || ' 00:00:00+00')::timestamptz + make_interval(secs => g * 86400.0 / :n) AS start,
        r1,
        r2 < 0.03 AS failed,
        CASE WHEN r3 < 0.1 THEN 'staging' ELSE 'production' END AS environment,
        -- A normal deviate from four uniforms, close enough for durations.
        CASE
            WHEN r2 < 0.03 THEN 1500000 + (r4 * 200000)::bigint
            ELSE (exp(ln(150) + 0.6 * ((r4 + r5 + r6 + r7 - 2) * 1.73)) * 1000)::bigint
        END AS duration_us
    FROM generate_series(0, :n - 1) AS g,
         LATERAL (SELECT random() AS r1, random() AS r2, random() AS r3, random() AS r4, random() AS r5,
                         random() AS r6, random() AS r7 WHERE g >= 0) AS r
),
root AS (
    SELECT trace.*,
           CASE WHEN r1 < 0.30 THEN 'checkout-web' WHEN r1 < 0.55 THEN 'orders-api' WHEN r1 < 0.75 THEN 'catalog-api'
                WHEN r1 < 0.90 THEN 'search' ELSE 'identity' END AS service,
           CASE WHEN r1 < 0.30 THEN 'POST /api/checkout' WHEN r1 < 0.55 THEN 'POST /orders'
                WHEN r1 < 0.75 THEN 'GET /products/{id}' WHEN r1 < 0.90 THEN 'GET /search' ELSE 'POST /tokens' END
               AS name,
           CASE WHEN r1 < 0.30 THEN '/api/checkout' WHEN r1 < 0.55 THEN '/orders' WHEN r1 < 0.75 THEN '/products/{id}'
                WHEN r1 < 0.90 THEN '/search' ELSE '/tokens' END AS route
    FROM trace
    WHERE start < :'until'::timestamptz
),
position (pos, parent, service, kind, name, starts_at, lasts, fails, attributes) AS (
    VALUES
        (0, NULL, NULL, 2, NULL, 0.0, 1.0, true, NULL::jsonb),
        (1, 0, NULL, 3, 'GET identity /tokens/introspect', 0.02, 0.12, false, '{}'),
        (2, 1, 'identity', 2, 'GET /tokens/introspect', 0.025, 0.11, false, '{"http.route": "/tokens/introspect"}'),
        (3, 2, 'identity', 3, 'SELECT identity.tokens', 0.03, 0.05, false, '{"db.system": "postgresql"}'),
        (4, 0, NULL, 3, 'POST payments-gateway /payments', 0.16, 0.8, true, '{}'),
        (5, 4, 'payments-gateway', 2, 'POST /payments', 0.165, 0.79, true, '{"http.route": "/payments"}'),
        (6, 5, 'payments-gateway', 3, 'SELECT payments.customer', 0.17, 0.04, false, '{"db.system": "postgresql"}'),
        (7, 5, 'payments-gateway', 3, 'POST psp-client /authorize', 0.22, 0.65, true, '{}'),
        (8, 7, 'psp-client', 2, 'POST /authorize', 0.225, 0.64, true, '{"http.route": "/authorize"}'),
        (9, 5, 'payments-gateway', 3, 'INSERT payments.payment_attempts', 0.88, 0.05, false,
         '{"db.system": "postgresql"}')
)
INSERT INTO storage.span
    (trace_id, span_id, parent_span_id, name, kind, start_time, duration_us, status, status_message, service_name,
     environment, host_name, attributes, resource_attributes, events, links, dropped_attributes, dropped_events,
     dropped_links)
SELECT
    root.trace_id,
    substr(decode(md5(root.key || '/' || position.pos), 'hex'), 1, 8),
    CASE WHEN position.parent IS NOT NULL THEN substr(decode(md5(root.key || '/' || position.parent), 'hex'), 1, 8) END,
    coalesce(position.name, root.name),
    position.kind,
    root.start + make_interval(secs => root.duration_us * position.starts_at / 1000000.0),
    greatest(1, (root.duration_us * position.lasts)::bigint),
    CASE WHEN root.failed AND position.fails THEN 2 ELSE 0 END,
    CASE WHEN root.failed AND position.pos = 0 THEN '502 from payments-gateway' END,
    coalesce(position.service, root.service),
    root.environment,
    'web-0' || (1 + root.g % 4),
    CASE
        WHEN position.pos = 0 THEN jsonb_build_object(
            'http.route', root.route,
            'http.request.method', split_part(root.name, ' ', 1),
            'http.response.status_code', CASE WHEN root.failed THEN '502' ELSE '200' END)
        ELSE position.attributes
    END,
    jsonb_build_object(
        'service.name', coalesce(position.service, root.service),
        'deployment.environment.name', root.environment),
    CASE
        WHEN root.failed AND position.pos = 8 THEN jsonb_build_array(jsonb_build_object(
            'time', root.start + make_interval(secs => root.duration_us * 0.86 / 1000000.0),
            'name', 'exception',
            'attributes', jsonb_build_object(
                'exception.type', 'System.TimeoutException',
                'exception.message', 'The PSP did not answer within 1.5 s')))
        ELSE '[]'::jsonb
    END,
    '[]'::jsonb,
    0,
    0,
    0
FROM root
CROSS JOIN position
ORDER BY 6;
