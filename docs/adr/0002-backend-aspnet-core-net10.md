# 0002. Backend: ASP.NET Core WebAPI on .NET 10, modular monolith

Date: 2026-11-07
Status: Accepted

## Context

The project owner's existing systems and expertise are .NET-based. The backend must handle sustained OTLP ingestion,
serve a query API, run alert evaluation, and host the web UI, all from one deployable unit for v0.1.

## Decision

Build the backend as an ASP.NET Core WebAPI targeting .NET 10 (LTS, supported to November 2028), structured as a modular
monolith with one project per module: `Core`, `Ingestion`, `Storage`, `Query`, `Alerting`, and the `Api` host.
Modules reference `Core` only and are composed in `Api`.

## Alternatives considered

- Microservices from the start: more operational surface, contradicts the "simple by default" principle.
- Single project with folders: no compiler-enforced boundaries, tends to degrade into tangled dependencies.
- .NET 8 LTS: still supported but ends earlier; no reason to start a new project on it.
- Non-.NET stacks (Go, Rust): viable for this problem class, but discard the owner's expertise and the planned .NET
  sample and Blazor telemetry integrations.

## Consequences

- One process, one container image, one deployment unit.
- Module boundaries are enforced by project references and can be split into services later if ever needed.
- Everything must build with the .NET 10 SDK; CI and the `Dockerfile` pin that version.
