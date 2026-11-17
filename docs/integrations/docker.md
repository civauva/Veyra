# Docker containers

A Linux Docker host sends Veyra each container's CPU, memory and its limit, network, block I/O, restarts, and uptime,
and every line the containers write to stdout and stderr (ADR 0018). Each container is a block of its own, named
after `container.name`, on the host in `host.name`. The tested configuration is
[`integrations/docker/collector.yaml`](../../integrations/docker/collector.yaml).

Tested with the Collector contrib distribution 0.140.1 in a container, against Docker 29.9 (API 1.56) under Docker
Desktop, and in CI against the Docker of a GitHub Ubuntu runner. Docker 25 or newer is needed: the configuration asks
for API version 1.44, the oldest Docker 29 still accepts.

## 1. Tag the logs with the container's name

Docker's JSON log files do not hold the container's name, only its ID. Tell the daemon to write the name into every
line as its tag, in `/etc/docker/daemon.json`:

```json
{
  "log-driver": "json-file",
  "log-opts": { "tag": "{{.Name}}", "max-size": "10m", "max-file": "3" }
}
```

```bash
sudo systemctl restart docker
```

The setting applies to containers **created** afterwards; recreate the others (`docker compose up -d
--force-recreate`, for example). Lines of a container without the tag still arrive, filed under the host instead of
the container. A single container can also be tagged on its own: `--log-opt tag={{.Name}}`, or in Compose:

```yaml
    logging:
      driver: json-file
      options:
        tag: "{{.Name}}"
```

`max-size` and `max-file` keep the files from filling the disk; Docker's default keeps everything. Docker Desktop
sets its daemon's settings under **Settings → Docker Engine**.

## 2a. From the Settings screen

1. Install a Veyra agent on the host: **Settings → Agents → Add an agent**, then run the one line it gives as root
   (see [`agents.md`](agents.md#an-agent-in-another-network)). The agent runs as the `veyra-agent` system user.
2. Let that user read Docker. The socket gives the container stats, and the log files under
   `/var/lib/docker/containers`, which only root may read, give the logs:

   ```bash
   sudo systemctl edit veyra-agent
   ```

   ```ini
   [Service]
   # The Docker socket, for each container's stats.
   SupplementaryGroups=docker
   # Read access to every file, for the containers' log files. Reading only: it cannot change anything.
   AmbientCapabilities=CAP_DAC_READ_SEARCH
   ```

   ```bash
   sudo systemctl restart veyra-agent
   ```

   Both are powerful. Whoever may use the Docker socket can start a container with the host's whole disk mounted,
   which is as much as root; and `CAP_DAC_READ_SEARCH` reads any file on the host. Grant them only on hosts whose
   containers you want Veyra to watch. Veyra's installer does not grant them by itself.
3. On the agent's card, switch on **Watch this host**, choose the host's environment, and switch on **Docker
   containers**. Save. The switch is shown on Linux agents only.

The agent gets its new configuration within half a minute. Turning **Watch this host** off turns Docker off too.

## 2b. With your own Collector

Create an ingestion key on Veyra's **Keys** page. Run the Collector in a container with the Docker socket and the log
folder mounted read-only, with the Linux host's own reads beside it as a second `--config`: containers are shown on
their host's screen, which a host without its own metrics does not have.

```bash
docker run -d --name veyra-docker --restart unless-stopped \
  --network host --pid host --user 0 \
  -v /:/hostfs:ro \
  -v /var/run/docker.sock:/var/run/docker.sock:ro \
  -v /var/lib/docker/containers:/var/lib/docker/containers:ro \
  -v "$PWD/integrations/linux/collector.yaml:/etc/veyra/linux.yaml:ro" \
  -v "$PWD/integrations/docker/collector.yaml:/etc/veyra/docker.yaml:ro" \
  -e HOST_ROOT=/hostfs \
  -e VEYRA_OTLP_ENDPOINT=http://127.0.0.1:4318 \
  -e VEYRA_ENVIRONMENT=production \
  --env-file /etc/veyra-docker.env \
  otel/opentelemetry-collector-contrib:0.140.0 \
  --config=/etc/veyra/linux.yaml --config=/etc/veyra/docker.yaml
```

- `/etc/veyra-docker.env` holds `VEYRA_INGESTION_KEY=vyk_...`, so the key stays out of the shell history.
- `--network host` gives `host.name` the host's own name, so the containers land on the same host as its metrics.
- `--user 0` lets the Collector read the socket and the log files, which belong to root.
- Both files define the same processors, the same way, so either can run alone or both together.

Installed on the host instead (the `.deb` package of
[`otelcol-contrib`](https://github.com/open-telemetry/opentelemetry-collector-releases/releases)), give the
`otelcol-contrib` service the same access as the Veyra agent in step 2a, and list both files with `--config` in
`/etc/otelcol-contrib/otelcol-contrib.conf`.

## What it sends

- **Container metrics** (`docker_stats`, every 30 seconds) for each running container, under `container.name`,
  `container.id`, `container.image.name`, `host.name`, and `deployment.environment.name`; never `service.name`:

  | Veyra shows | Metric | Unit and kind |
  | --- | --- | --- |
  | CPU | `container.cpu.utilization` | gauge, percent of one CPU: 100 is one core busy, 200 two |
  | Memory in use | `container.memory.usage.total` | bytes, without the page cache |
  | Memory limit | `container.memory.usage.limit` | bytes; the host's memory when the container has no limit |
  | Memory of its limit | `container.memory.percent` | gauge, percent 0 to 100 |
  | Restarts | `container.restarts` | cumulative count since the container was created |
  | Uptime | `container.uptime` | seconds since it last started |
  | Network | `container.network.io.usage.rx_bytes`, `.tx_bytes` | cumulative bytes, per interface |
  | Disk I/O | `container.blockio.io_service_bytes_recursive` | cumulative bytes, once the container used a disk |

  `container.restarts` counts the restarts made by the container's restart policy; `docker restart` does not count,
  and a recreated container starts again from 0. A container caught between two restarts reports 0 for its CPU and
  memory, its limit too.
- **Container logs** (`filelog`): every new line of every container, its text as the body, `log.iostream` saying
  `stdout` or `stderr`, and the line's own time. Its resource holds `container.name` (from the tag) and
  `container.id`. Lines longer than 16 KiB, which Docker splits, arrive whole. Lines on stderr are not errors by
  themselves, and no severity is guessed from the text. An application in a container that sends its own telemetry
  with an OpenTelemetry SDK keeps its service; only its stdout and stderr belong to the container.
- **Not sent:** stopped containers, and the lines a container wrote before the Collector started.
- **Volume.** About 15 series per container, so about 43,000 points a day per container, plus its log lines.

## When Veyra is unreachable

As for a Linux host: the exporter holds up to 1,000 batches in memory and retries each for up to 10 minutes, and the
memory limiter keeps the Collector under 128 MiB. The Collector keeps no place in the log files on disk: after it
restarts, it reads each file from its end, so lines written while it was stopped are not sent.

## Check it

Within a minute the containers appear on the host. If they do not:

- `journalctl -u veyra-agent` (or `docker logs veyra-docker`) shows `permission denied` on `/var/run/docker.sock`
  or on the log files when the access of step 2a is missing, and `client version 1.44 is too new` on a Docker older
  than 25.
- Container logs filed under the host instead of the container miss the tag: check `daemon.json` and recreate the
  container. `docker inspect --format '{{.HostConfig.LogConfig}}' <container>` shows its log settings.
- The Collector logs `Could not parse docker containerStats ... context canceled` now and then, mostly right after it
  starts; that container misses one point, which is harmless.

The same check as CI starts three containers, one that logs, one that keeps restarting, and one that keeps a CPU
busy, runs the Collector beside them for 75 seconds, and checks what it would send. It needs Docker and Node.js:

```bash
bash integrations/docker/test/run.sh
```
