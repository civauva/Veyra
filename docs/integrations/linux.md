# Linux hosts

The OpenTelemetry Collector watches a Linux host with its `hostmetrics` receiver and sends CPU, memory, load,
disks, disk I/O, and network to Veyra over OTLP. Veyra needs nothing on the host besides the Collector. The tested
configuration is [`integrations/linux/collector.yaml`](../../integrations/linux/collector.yaml).

Tested with the Collector contrib distribution 0.140.0, running in a container.

## 1. Create a key

Create an ingestion key on Veyra's **Keys** page, limited to the host's environment if you like. The secret is shown
once.

## 2a. Run the Collector on the host

Install the contrib distribution (`otelcol-contrib`) from the
[Collector releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases), for example the
`.deb` package, then:

```bash
sudo cp integrations/linux/collector.yaml /etc/otelcol-contrib/config.yaml
sudo nano /etc/otelcol-contrib/otelcol-contrib.conf
```

The package's service reads its environment from that file. Add the settings, then restart the service:

```bash
VEYRA_OTLP_ENDPOINT=https://veyra.example.com
VEYRA_INGESTION_KEY=vyk_...
VEYRA_ENVIRONMENT=production
HOST_ROOT=/
```

```bash
sudo systemctl restart otelcol-contrib
```

Keep the file readable by root only (`chmod 600`); the key is a secret.

## 2b. Or run it in a container

The Collector reads the host's `/proc` and file systems through a read-only mount of the host's root:

```bash
docker run -d --name veyra-hostmetrics --restart unless-stopped \
  --network host --pid host \
  -v /:/hostfs:ro \
  -v "$PWD/integrations/linux/collector.yaml:/etc/otelcol-contrib/config.yaml:ro" \
  -e HOST_ROOT=/hostfs \
  -e VEYRA_OTLP_ENDPOINT=http://127.0.0.1:4318 \
  -e VEYRA_ENVIRONMENT=production \
  --env-file /etc/veyra-hostmetrics.env \
  otel/opentelemetry-collector-contrib:0.140.0
```

`/etc/veyra-hostmetrics.env` holds `VEYRA_INGESTION_KEY=vyk_...`, so the key stays out of the shell history. With
`--network host`, a Collector on the Veyra server reaches Veyra's local OTLP port without TLS; anywhere else, use
`https://<veyra host>`.

## What it sends

Every 30 seconds:

| Veyra shows | Metric |
| --- | --- |
| CPU busy, by user, system, and iowait | `system.cpu.time` |
| Load 1, 5, 15 minutes | `system.cpu.load_average.1m`, `.5m`, `.15m` |
| Memory used and cache | `system.memory.usage` |
| Disks, and when each fills up | `system.filesystem.usage` |
| Disk reads and writes | `system.disk.io` |
| Network received and sent | `system.network.io` |

- **Host name and OS.** `resourcedetection` sets `host.name` and `os.description` from the host. The host's card on
  the **Metrics** page is named after `host.name`.
- **Disks.** Pseudo file systems (`tmpfs`, `overlay`, `proc`, and so on) and mounts under `/dev`, `/proc`, `/sys`,
  `/run`, `/snap`, and `/var/lib/docker` are left out. Veyra estimates when a disk fills up from the last day of
  usage, and the Forecast home's outlook warns about any that fill up within 30 days. The built-in disk rules raise
  a caution when a disk is 90 % full or full within 14 days, and a warning at 95 % or within 3 days
  ([alerting.md](../architecture/alerting.md)).
- **Volume.** About 100 series per host, under 300,000 points a day; a few megabytes of storage per host per day.

## When Veyra is unreachable

The `otlphttp` exporter holds up to 1,000 batches in memory and retries each one for up to 10 minutes; a batch that
still fails after that is dropped, so an outage longer than 10 minutes leaves a gap. Nothing is written to the
host's disk. The memory limiter keeps the Collector under 128 MiB. Veyra answers `503` while its metric queue is
full, and the exporter retries those too.

## Check it

Within a minute, the host appears on the **Metrics** page. If it does not:

- `journalctl -u otelcol-contrib` (or `docker logs veyra-hostmetrics`) shows `401` for a wrong key and connection
  errors for a wrong endpoint.
- The host card shows "not reported" for disks when the Collector cannot see the host's file systems: in a container,
  check the `/:/hostfs:ro` mount and `HOST_ROOT=/hostfs`.
