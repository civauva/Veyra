-- Benchmark (ADR 0015): the metric series of 10 hosts (the Collector's hostmetrics receiver) and 10 .NET services (the
-- OpenTelemetry SDK), about 1,500 series, as they would be after a month. Run once, before the points.
\set ON_ERROR_STOP on

TRUNCATE storage.metric_series;

WITH hosts AS (
    SELECT format('web-%s', lpad(h::text, 2, '0')) AS host FROM generate_series(1, 10) AS h
),
host_series (name, kind, unit, temporality, monotonic, attributes) AS (
    SELECT 'system.cpu.time', 2, 's', 2, true, jsonb_build_object('cpu', format('cpu%s', c), 'state', s)
    FROM generate_series(0, 3) AS c, unnest(ARRAY['user', 'system', 'idle', 'wait']) AS s
    UNION ALL
    SELECT 'system.memory.usage', 1, 'By', 0, false, jsonb_build_object('state', s)
    FROM unnest(ARRAY['used', 'free', 'cached', 'buffered']) AS s
    UNION ALL
    SELECT 'system.filesystem.usage', 1, 'By', 0, false,
        jsonb_build_object('device', d, 'mountpoint', m, 'type', 'ext4', 'mode', 'rw', 'state', s)
    FROM (VALUES ('/dev/sda1', '/'), ('/dev/sdb1', '/data'), ('/dev/sda2', '/var/log')) AS f (d, m),
        unnest(ARRAY['used', 'free', 'reserved']) AS s
    UNION ALL
    SELECT 'system.network.io', 2, 'By', 2, true, jsonb_build_object('device', 'eth0', 'direction', dir)
    FROM unnest(ARRAY['receive', 'transmit']) AS dir
    UNION ALL
    SELECT 'system.disk.io', 2, 'By', 2, true, jsonb_build_object('device', d, 'direction', dir)
    FROM unnest(ARRAY['sda', 'sdb']) AS d, unnest(ARRAY['read', 'write']) AS dir
    UNION ALL
    -- The rest of hostmetrics: load, paging, processes, and the like, about 60 series more.
    SELECT format('system.extra.metric_%s', x), 1, '1', 0, false, '{}'::jsonb FROM generate_series(1, 60) AS x
),
services AS (
    SELECT s AS service
    FROM unnest(ARRAY['checkout-web', 'orders-api', 'payments-gateway', 'search', 'catalog-api', 'identity',
                      'notifications', 'billing-worker', 'reporting', 'inventory']) AS s
),
service_series (name, kind, unit, temporality, monotonic, attributes) AS (
    SELECT 'http.server.request.duration', 3, 's', 2, false,
        jsonb_build_object('http.route', r, 'http.request.method', m, 'http.response.status_code', c)
    FROM unnest(ARRAY['/checkout/confirm', '/orders/{id}', '/products', '/pay', '/search']) AS r,
        unnest(ARRAY['GET', 'POST']) AS m, unnest(ARRAY['200', '500']) AS c
    UNION ALL
    SELECT 'process.cpu.time', 2, 's', 2, true, jsonb_build_object('cpu.mode', mode)
    FROM unnest(ARRAY['user', 'system']) AS mode
    UNION ALL
    SELECT 'process.memory.usage', 1, 'By', 0, false, '{}'::jsonb
    UNION ALL
    SELECT 'dotnet.gc.collections', 2, '{collection}', 2, true, jsonb_build_object('dotnet.gc.heap.generation', g)
    FROM unnest(ARRAY['gen0', 'gen1', 'gen2']) AS g
    UNION ALL
    SELECT 'dotnet.thread_pool.thread.count', 1, '{thread}', 0, false, '{}'::jsonb
    UNION ALL
    SELECT 'dotnet.thread_pool.queue.length', 1, '{work_item}', 0, false, '{}'::jsonb
    UNION ALL
    SELECT 'dotnet.process.memory.working_set', 1, 'By', 0, false, '{}'::jsonb
    UNION ALL
    SELECT format('dotnet.extra.metric_%s', x), 1, '1', 0, false, '{}'::jsonb FROM generate_series(1, 120) AS x
),
everything AS (
    SELECT hs.name, hs.kind, hs.unit, hs.temporality, hs.monotonic, NULL::text AS service, NULL::text AS environment,
        h.host, jsonb_build_object('host.name', h.host, 'os.type', 'linux') AS resource, hs.attributes
    FROM hosts AS h, host_series AS hs
    UNION ALL
    SELECT ss.name, ss.kind, ss.unit, ss.temporality, ss.monotonic, s.service, 'production', NULL,
        jsonb_build_object('service.name', s.service, 'deployment.environment.name', 'production',
                           'telemetry.sdk.language', 'dotnet'),
        ss.attributes
    FROM services AS s, service_series AS ss
)
INSERT INTO storage.metric_series
    (hash, name, kind, unit, description, temporality, monotonic, service_name, environment, host_name,
     resource_attributes, attributes, first_seen, last_seen)
SELECT
    sha256(convert_to(name || coalesce(service, '') || coalesce(host, '') || attributes::text, 'UTF8')),
    name, kind, unit, '', temporality, monotonic, service, environment, host, resource, attributes,
    now() - interval '30 days', now()
FROM everything;

SELECT count(*) AS series FROM storage.metric_series;
