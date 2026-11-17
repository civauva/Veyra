# Veyra agents

A Veyra agent reads systems for Veyra and sends what it reads over OTLP. It is the official OpenTelemetry Collector run
by the official OpAMP supervisor, unchanged, at the version Veyra tests (0.140.0). Veyra configures it over OpAMP:
nobody edits its files (ADR 0019). Today agents read SQL Servers and PostgreSQL servers; see
[`sqlserver.md`](sqlserver.md) and [`postgres.md`](postgres.md).

An agent reads a SQL Server over the network, on its port, so nothing is installed on the database server itself.

## The built-in agent

Veyra's Docker Compose deployment runs one agent beside Veyra: the `agent` service in `deploy/docker-compose.yml`.
Veyra sets it up on its first start and gives it its own key. It reads every server Veyra's host can reach.
**Settings → Agents** shows it, and under **How the built-in agent runs** has the commands to check, start, and read
it.

## An agent in another network

When Veyra cannot reach a SQL Server, as when Veyra runs on a public server and the database sits behind a company
firewall, install an agent inside that network. One agent reads every server of its network. It only connects out:
to Veyra over HTTPS, and to its SQL Servers. Nothing connects in.

1. Open **Settings → Agents** and choose **Add an agent**. Name it after its network, such as "Company network".
2. The page shows a one-time code, which works once, for an hour, and the line to run. Choose Windows or Linux.
3. Run the line on any machine in that network that reaches its SQL Servers:
   - **Windows**, in PowerShell as an administrator:

     ```powershell
     irm https://veyra.example.com/v1/agents/install.ps1 | iex; Install-VeyraAgent -Code K7QM-4TWX-9PRA
     ```

   - **Linux** with systemd, as root:

     ```bash
     curl -fsSL https://veyra.example.com/v1/agents/install.sh | sudo sh -s -- --code K7QM-4TWX-9PRA
     ```

4. The page shows the agent as connected within a minute. Add its SQL Servers under **Settings → Database
   monitoring** and choose it under **Read by**.

The machine needs outbound HTTPS to Veyra and to `github.com`, and access to its SQL Servers' ports.

### What the installer does

1. Downloads the supervisor and the Collector from
   [OpenTelemetry's releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases), and checks
   each against the SHA-256 that Veyra pins in its code with the version. A file that does not match is deleted and
   nothing is installed.
2. Exchanges the code for the agent's own ingestion key and its start-up file. A used, wrong, or expired code is
   refused; after ten wrong codes from one address in a minute, Veyra stops answering it for the rest of the minute.
3. Installs the programs and the service, and starts it.

| | Windows | Linux |
| --- | --- | --- |
| Programs | `C:\Program Files\Veyra Agent` | `/opt/veyra-agent` |
| Start-up file, with the key | `C:\ProgramData\Veyra Agent\supervisor.yaml` | `/etc/veyra-agent/supervisor.yaml` |
| Supervisor's state | `C:\ProgramData\Veyra Agent\state` | `/var/lib/veyra-agent` |
| Service | `veyra-agent`, "Veyra agent", starts with Windows | `veyra-agent.service` |
| Runs as | Local System | the `veyra-agent` system user |
| Readable by | the system and administrators only | that user and root only |
| Its own log | the Application event log, source `veyra-agent` | `journalctl -u veyra-agent` |

Running the installer again with a new code reinstalls the agent in place: it gets a new key and the old one is
revoked. Files downloaded before are used again when they still match.

### Change or remove an agent

- **New code** on an agent that never connected, or went offline, installs it again, on the same machine or another.
- **Remove** revokes the agent's key, so it can no longer connect or send. An agent that still reads database servers
  cannot be removed; give them to another agent first. Then uninstall it on its machine:

  ```powershell
  irm https://veyra.example.com/v1/agents/install.ps1 | iex; Uninstall-VeyraAgent
  ```

  ```bash
  curl -fsSL https://veyra.example.com/v1/agents/install.sh | sudo sh -s -- --uninstall
  ```

### Watch the agent's own host

An installed agent can also read the machine it runs on: switch on **Watch this host** on its card. A Windows agent
can add its Event Log errors and IIS ([`windows.md`](windows.md)); a Linux agent its Docker containers, once its user
may read Docker ([`docker.md`](docker.md#2a-from-the-settings-screen)). The built-in agent runs in a container and
cannot watch a host.

### Who changed what

**History** on the Settings screen lists every agent, SQL Server, and PostgreSQL server added, changed, or removed,
under **Agents** and **Database monitoring**: who did it, when, and which fields changed, such as "Changed port,
password". A new code and a change to what an agent watches count as changes. Values are never kept there, so no
password or address appears, and these changes cannot be reverted from the history.

## States

| State | Meaning |
| --- | --- |
| Online | It polls Veyra and runs Veyra's configuration. |
| Not installed yet | Added on the page; no installer has used its code. |
| Not connected yet | Installed, or built in, but it has never reached Veyra. |
| Offline | It has not polled Veyra for two minutes. |
| Refused its settings | Its Collector refused the configuration Veyra sent; it keeps its last one. |
| Unhealthy | Its Collector reports a component that does not run. |

Agents poll Veyra every 30 seconds, so a change on the Settings screen reaches them within that time. While Veyra is
down, an agent keeps reading with its last configuration and sends again once Veyra answers.
