# 0019. The Veyra agent: monitoring targets set up from the GUI

Date: 2026-11-13
Status: Accepted

## Context

ADR 0018 watches databases, containers, and Windows hosts through the OpenTelemetry Collector, with tested
configurations in `integrations/` that read their values from environment variables. The first of them, SQL Server
(VEY-34), works, but setting it up means installing a Collector, copying a file, and setting ten variables by hand.

The person running Veyra wants every target set up from Veyra's GUI, with as few config files as possible
(VEY-38). Settings already live in the database and are edited on the Settings screen (ADR 0017). Monitored systems
are the gap.

Facts that bound the choice:

- **Someone must reach the target.** SQL Server is read over its port, 1433. When Veyra runs inside the same network,
  it can reach it. When Veyra runs elsewhere, such as the reference installation on a public server, firewalls keep
  1433 closed to it, so a reader has to sit inside that network and connect out. Commercial tools solve this the
  same way: an agent, or a "central monitoring service" installed inside the network.
- **Nothing has to run on the database servers.** The Collector reads SQL Server remotely, over the network.
- **OpenTelemetry already has remote management.** OpAMP is its protocol for a server to manage agents: send them
  configuration, learn their health and versions. The Collector contrib project ships an OpAMP supervisor that starts
  the Collector, takes its configuration from an OpAMP server, and reports back. Both are Apache 2.0 and published as
  images at the version Veyra pins, 0.140.0.

A test on 2026-11-13 ran the official supervisor and Collector against a real SQL Server 2022, with a small OpAMP
server in Veyra's place sending the whole SQL Server configuration, values written in, no environment variables:

- The agent applied the configuration within a second of receiving it, came up healthy, and reported every component.
  The SQL Server's figures arrived in Veyra.
- A changed configuration was applied the same way: the supervisor restarted its Collector by itself, healthy again
  within a second.
- After the OpAMP server restarted, it asked for the agent's full state and got it.
- While the OpAMP server was down, the agent kept collecting with its last configuration and retried.
- A wrong password does **not** show in OpAMP health: the Collector stays healthy and logs
  `Login failed for user 'veyra_monitor'` on every scrape. The error is in the Collector's own logs only.
- The supervisor keeps the configuration it received, password included, in files on the agent's disk that only its
  own user can read (`0600`).

## Decision

Veyra manages its own collectors. A **Veyra agent** is the official OpenTelemetry Collector run by the official OpAMP
supervisor, configured entirely by Veyra over OpAMP. People add and change monitored systems on the Settings screen;
Veyra writes each agent's Collector configuration and sends it. Nobody edits a config file or sets a variable.

### The agent

- **Upstream builds only.** The agent is `otelcol-contrib` and `opampsupervisor` at the version Veyra pins and tests in
  CI, 0.140.0 now. Veyra writes no code that runs inside it. A slimmer build of only the components Veyra uses may come
  later, with the Collector's own builder; it is not part of this decision.
- **Two places it runs**, both managed the same way:
  - **Built in.** A second container in Veyra's Docker Compose (ADR 0006), enrolled automatically at startup. It reads
    every target Veyra itself can reach. Inside a company's walls this is the only agent needed: nothing is installed
    anywhere else.
  - **In another network.** For targets Veyra cannot reach. The Settings screen gives a one-time enrollment code and the
    way to install: a Windows installer that asks only for the code, or one line for PowerShell, Linux, or Docker. The
    agent connects out to Veyra over HTTPS; nothing connects in. One agent reads every target of its network.
- **First version ships no binaries of its own.** The install line and the built-in container use OpenTelemetry's
  published images and packages at the pinned version, checked by checksum. A Veyra Windows installer, served by Veyra
  itself, is a later step; it changes the packaging, not this design.

### OpAMP in Veyra

- A new module, **`Agents`**, owns the OpAMP server, its schema `agents`, and its configuration (ADR 0017). It
  references `Veyra.Core` only, like every module (AGENTS.md section 4).
- Veyra speaks OpAMP over plain HTTP at `/v1/opamp`, behind the same address and TLS as the API. Agents poll every 30
  seconds; a change reaches an agent within that time. WebSocket, which would make it immediate, can come later.
- **Enrollment.** A code from the Settings screen works once, for 60 minutes. With it, the agent gets its own agent key,
  stored hashed as ingestion keys are (ADR 0008). The key signs it in to OpAMP and to OTLP. Revoking it on the
  Agents page cuts the agent off.
- **Configuration.** For each agent, Veyra builds one Collector configuration from the targets assigned to it, out of
  the tested templates in `integrations/`, with the values written in. It sends it only when it changed, with its
  hash, and shows whether the agent applied it.
- **Secrets.** Passwords of monitored systems are stored encrypted with ASP.NET Core Data Protection, as webhook URLs
  and the SMTP password are (ADR 0009, ADR 0017), shown to nobody, and sent only to the agent that reads that target.
  The agent keeps its configuration on its disk, readable only by its own user; that is where a password must be for
  the Collector to use it, as with environment variables today.

### What people see

- **Settings → Agents.** Every agent with its state (online, offline, configuration failed), place, version, and what
  it reads, and the steps to add one.
- **Settings → Database monitoring.** SQL Server now, PostgreSQL with VEY-33. A server is added in one panel:
  - where it is (name in Veyra, address, port, environment, which agent reads it);
  - the login: the page makes a password and writes the script that creates the login with exactly the grants of the
    SQL Server guide, to run once in SQL Server Management Studio, since only an administrator of that server can;
  - encryption: required, trust the server's own certificate, or off;
  - a check before saving: the agent tries the connection and the reads, and the panel lists what worked.
- **State per server**, in words: receiving, waiting for first data, login failed, server not reachable, a grant
  missing, agent offline. Health from OpAMP says whether the agent runs; the reasons come from the **agent's own
  logs**, which the agent sends to Veyra like any telemetry and Veyra matches to the target, and from whether the
  target's data arrives. The test showed health alone cannot tell a wrong password.
- The alert rules of ADR 0018 stay under Alert rules; the database page links to them.

### Plain Collectors stay

`integrations/` and its guides remain the contract for anyone who runs their own Collector: telemetry with the same
attributes lands in the same blocks and rules (ADR 0018). The Settings screen marks such targets as "sent by your own
Collector" and does not manage them.

## Alternatives considered

- **Keep environment variables, with a guided page that writes them.** Small and safe, but every change still means
  editing and restarting something by hand, which is what the person asked to avoid.
- **Veyra reads targets itself, without the Collector.** Every receiver would be rewritten in .NET, against the
  OTLP-only decision, and Veyra could still not reach targets behind a firewall.
- **Only a built-in collector.** Enough inside one network, impossible for targets in another one.
- **A Veyra fork or custom build of the Collector.** Smaller downloads, but a release to follow every few weeks. Kept
  for later, only if the upstream builds prove too heavy.
- **OpAMP over WebSocket from the start.** Immediate changes, but a long-lived connection through proxies and Caddy to
  operate. Polling every 30 seconds is enough for configuration.
- **Agents pull secrets from Veyra at runtime instead of receiving them in configuration.** The password would still
  sit in the agent's memory and, through the Collector, nowhere safer; the extra protocol gains little.

## Consequences

- Monitored systems are set up, changed, and checked on the Settings screen. VEY-32, VEY-33, and VEY-35 build on this
  instead of adding more environment variables.
- Veyra holds credentials to monitored systems. They are encrypted at rest, never shown back, sent to one agent only,
  and their changes are recorded in the settings history without the values.
- A second container joins the Docker Compose deployment. Its version moves with Veyra's, and CI tests the generated
  configurations against real servers, as `integrations/sqlserver/test/run.sh` does now.
- The OpAMP supervisor is still marked early by OpenTelemetry. Veyra pins its version and tests every upgrade; a
  supervisor change that breaks the protocol is caught in CI before release.
- A new module, `Agents`, with its own schema and migrations, and a new endpoint, `/v1/opamp`. AGENTS.md section 4
  gains the module.
- The `GODEBUG` setting a self-signed SQL Server certificate needs (VEY-34) is part of the agent as installed, in its
  container and its installer; nobody sets it by hand.
- Untested yet: the agent sending its own logs to Veyra, which the reasons above rely on. The supervisor offers it
  through OpAMP connection settings; the first build proves it against a wrong password and a missing grant before the
  states are shown.
