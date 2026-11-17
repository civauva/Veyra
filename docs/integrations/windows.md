# Windows hosts and IIS

A Windows host sends Veyra its CPU, memory, disks, page file, and network, the errors of its System and Application
Event Logs, and, with IIS, every site's requests (ADR 0018). The tested configurations are
[`integrations/windows/collector.yaml`](../../integrations/windows/collector.yaml) and, for IIS,
[`integrations/windows/iis.yaml`](../../integrations/windows/iis.yaml).

Tested with the Collector contrib distribution 0.140.1 on Windows 11, and in CI on a Windows Server runner with IIS.

## From the Settings screen

1. Install a Veyra agent on the host: **Settings → Agents → Add an agent**, then run the one line it gives in
   PowerShell as an administrator (see [`agents.md`](agents.md#an-agent-in-another-network)). The agent runs as the
   Windows service `veyra-agent`.
2. On the agent's card, switch on **Watch this host** and choose the host's environment.
3. Choose what else to read: **Event Log errors** and, when IIS runs on the host, **IIS sites**. Save.

The agent gets its new configuration within half a minute. The host appears under Hosts on the **Metrics** page, its
recent errors on its own page, and each IIS site under Services. The same agent can also read SQL Servers.

## With your own Collector

Install the contrib distribution (`otelcol-contrib`) from the
[Collector releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases), for example its
`.msi`, which installs the Windows service `otelcol-contrib`. Create an ingestion key on Veyra's **Keys** page, then
set the service's environment and arguments in PowerShell as an administrator:

```powershell
$key = "HKLM:\SYSTEM\CurrentControlSet\Services\otelcol-contrib"
Set-ItemProperty $key Environment -Type MultiString -Value @(
  "VEYRA_OTLP_ENDPOINT=https://veyra.example.com",
  "VEYRA_INGESTION_KEY=vyk_...",
  "VEYRA_ENVIRONMENT=production")
```

Copy both files to `C:\Program Files\OpenTelemetry Collector\`, point the service at them (leave out `iis.yaml`
without IIS), and restart it:

```powershell
sc.exe config otelcol-contrib binPath= "`"C:\Program Files\OpenTelemetry Collector\otelcol-contrib.exe`" --config `"C:\Program Files\OpenTelemetry Collector\collector.yaml`" --config `"C:\Program Files\OpenTelemetry Collector\iis.yaml`""
Restart-Service otelcol-contrib
```

The key is a secret: only administrators can read the service's registry key.

## What it sends

- **Host metrics** (`hostmetrics`, every 30 seconds): `system.cpu.time`, `system.memory.usage`,
  `system.filesystem.usage` per drive, `system.disk.io`, `system.network.io`, and `system.paging.usage`, under
  `host.name` and `deployment.environment.name`. They fill the host's card and page like a Linux host's, and the
  disk-space and disk-filling rules watch its drives.
- **Event Log errors** (`windowseventlog`): new events of the System and Application logs at Error and Critical;
  warnings and information stay on the host. Each is a log of the host, its text the event's message, with
  `windows.event.id`, `windows.event.provider`, and `windows.event.channel`. An event whose provider's message files
  are gone, such as an uninstalled driver's, reads `Event 7023 from Service Control Manager`; the Collector logs an
  error about it, which is harmless.
- **IIS sites** (`iis.yaml`): every request in a site's W3C log becomes a log of that site's service, named after its
  log folder, such as `W3SVC1` for the Default Web Site (IIS Manager lists each site's ID). Status 5xx is an error and
  4xx a warning, so the error-rate rule watches each site. Each carries `http.request.method`, `url.path`,
  `http.response.status_code`, `client.address`, and `iis.time_taken_ms`. IIS must log in the W3C format with its
  default fields, as it does unless changed; it writes its log about once a minute, so requests arrive that late. The
  `iis` receiver adds IIS's own figures to the host: requests, connections, queues, and application pool state per
  site.

## Check it

Within a minute the host appears under Hosts on the **Metrics** page. The same check as CI runs the Collector on this
machine for 45 seconds and checks what it would send, without IIS against a sample W3C log; it needs Node.js:

```powershell
powershell -ExecutionPolicy Bypass -File integrations\windows\test\run.ps1 -Collector C:\otelcol\otelcol-contrib.exe
```
