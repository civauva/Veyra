-- Benchmark (ADR 0015): one day of log records, as a busy small installation sends them, in time order as ingestion
-- writes them. psql variables: day (YYYY-MM-DD, a UTC day), n (records), until (an instant; records stop there, so
-- today ends at now). The day is left for Veyra's backfill to count, as after an upgrade.
-- Twelve services with uneven traffic, three environments, 15 % debug, 70 % info, 10 % warning, 4.5 % error,
-- 0.5 % fatal, about 40 message shapes with numbers in them, attributes like real HTTP and job logs, and trace IDs on
-- four records in ten.
\set ON_ERROR_STOP on

SELECT format(
    'CREATE TABLE IF NOT EXISTS storage.log_record_%s PARTITION OF storage.log_record FOR VALUES FROM (%L) TO (%L)',
    to_char(:'day'::date, 'YYYYMMDD'),
    (:'day'::date)::timestamptz AT TIME ZONE 'UTC',
    (:'day'::date + 1)::timestamptz AT TIME ZONE 'UTC') \gexec

-- The counts get the same days, as Veyra makes them.
SELECT format(
    'CREATE TABLE IF NOT EXISTS storage.%s_%s PARTITION OF storage.%s FOR VALUES FROM (%L) TO (%L)',
    t, to_char(:'day'::date, 'YYYYMMDD'), t,
    (:'day'::date)::timestamptz AT TIME ZONE 'UTC',
    (:'day'::date + 1)::timestamptz AT TIME ZONE 'UTC')
FROM unnest(ARRAY['log_count_minute', 'log_count_hour']) AS t \gexec

SET synchronous_commit = off;

INSERT INTO storage.log_record
    (timestamp, observed_at, severity, severity_text, body, service_name, environment, trace_id, span_id,
     attributes, resource_attributes)
SELECT
    ts,
    ts + interval '40 milliseconds',
    sev,
    NULL,
    CASE
        WHEN sev >= 5 THEN (ARRAY[
            format('Payment declined by PSP: upstream timeout after %s ms', 1000 + (r3 * 3000)::int),
            format('Unhandled exception: System.TimeoutException: The operation timed out after %s ms', (r3 * 30000)::int),
            format('Database query failed: deadlock detected on relation orders (pid %s)', (r3 * 90000)::int),
            format('Order %s could not be reserved: stock service returned 503', (r3 * 900000)::int),
            format('Failed to publish message %s to queue billing: connection reset', (r3 * 900000)::int),
            format('Token validation failed for client %s: signature expired', (r3 * 5000)::int)
        ])[1 + floor(r2 * 6)::int]
        WHEN sev = 4 THEN (ARRAY[
            format('Slow response from PSP /authorize: %s ms', 800 + (r3 * 2000)::int),
            format('Retrying acquirer call, attempt %s of 3', 1 + (r3 * 2)::int),
            format('Cache miss rate %s%% above threshold', 20 + (r3 * 60)::int),
            format('Request queue length %s', 50 + (r3 * 500)::int),
            format('Payment step slow: %s ms', 1500 + (r3 * 3000)::int)
        ])[1 + floor(r2 * 5)::int]
        WHEN sev = 2 THEN (ARRAY[
            format('Picked job invoice-%s from queue billing', (r3 * 900000)::int),
            format('Cache hit for product %s', (r3 * 50000)::int),
            format('Opened connection %s to postgres', (r3 * 2000)::int),
            format('Job invoice-%s done in %s ms', (r3 * 900000)::int, (r2 * 900)::int)
        ])[1 + floor(r2 * 4)::int]
        ELSE (ARRAY[
            format('GET /products?category=%s 200 in %s ms', (ARRAY['shoes','bags','coats','hats'])[1 + floor(r3 * 4)::int], 5 + (r2 * 400)::int),
            format('POST /checkout/confirm 200 %s ms', 100 + (r3 * 600)::int),
            format('Order %s created for customer %s', (r3 * 900000)::int, (r2 * 50000)::int),
            format('GET /orders/%s 200 %s ms', (r3 * 900000)::int, 3 + (r2 * 80)::int),
            format('Token refreshed for client %s', (ARRAY['checkout-web','mobile-app','partner-api'])[1 + floor(r3 * 3)::int]),
            format('Query "%s" %s hits %s ms', (ARRAY['running shoes','winter coat','leather bag'])[1 + floor(r3 * 3)::int], (r2 * 200)::int, 5 + (r3 * 60)::int),
            format('Email queued for order %s', (r3 * 900000)::int),
            format('Index refresh %s documents', (r3 * 400)::int),
            format('Sign-in succeeded for user %s', (r3 * 20000)::int),
            format('Daily revenue rollup finished in %s s', round((r3 * 9)::numeric, 1))
        ])[1 + floor(r2 * 10)::int]
    END,
    svc,
    env,
    CASE WHEN r4 < 0.4 THEN md5(r4::text || g::text) END,
    CASE WHEN r4 < 0.4 THEN substr(md5(g::text || r3::text), 1, 16) END,
    CASE
        WHEN svc IN ('checkout-web', 'orders-api', 'payments-gateway', 'search', 'catalog-api') THEN jsonb_build_object(
            'http.route', (ARRAY['/checkout/confirm','/orders/{id}','/products','/pay','/search'])[1 + floor(r2 * 5)::int],
            'http.request.method', CASE WHEN r3 < 0.7 THEN 'GET' ELSE 'POST' END,
            'http.response.status_code', CASE WHEN sev >= 5 THEN '500' WHEN r3 < 0.05 THEN '404' ELSE '200' END)
        ELSE jsonb_build_object('job.name', (ARRAY['invoice','reminder','rollup','sync'])[1 + floor(r2 * 4)::int])
    END,
    jsonb_build_object(
        'service.name', svc,
        'deployment.environment', env,
        'host.name', format('web-%s', lpad((1 + (r4 * 9)::int)::text, 2, '0')))
FROM (
    SELECT
        g,
        (:'day' || ' 00:00:00+00')::timestamptz + random() * least(interval '1 day', :'until'::timestamptz - (:'day' || ' 00:00:00+00')::timestamptz) AS ts,
        CASE
            WHEN r1 < 0.15 THEN 2
            WHEN r1 < 0.85 THEN 3
            WHEN r1 < 0.95 THEN 4
            WHEN r1 < 0.995 THEN 5
            ELSE 6
        END::smallint AS sev,
        -- Uneven traffic: the first services send most.
        (ARRAY['checkout-web','orders-api','payments-gateway','search','catalog-api','identity','notifications',
               'billing-worker','reporting','inventory','shipping','psp-client'])[1 + floor(12 * power(random(), 1.6))::int] AS svc,
        CASE WHEN r2 < 0.7 THEN 'production' WHEN r2 < 0.95 THEN 'staging' ELSE 'development' END AS env,
        r2, r3, r4
    FROM (
        SELECT g, random() AS r1, random() AS r2, random() AS r3, random() AS r4
        FROM generate_series(1, :n) AS g
    ) AS dice
) AS rows
ORDER BY ts;
