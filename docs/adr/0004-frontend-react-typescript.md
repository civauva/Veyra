# 0004. Frontend: React + TypeScript with Vite

Date: 2026-11-07
Status: Accepted

## Context

The UI is the product: an Overview, a Log Explorer with virtualized tables and live updates, incident details, and
metric charts, in light and dark themes. The backend is .NET, so Blazor was a natural candidate.

## Decision

Build the frontend as a React + TypeScript single-page application with Vite. The production build is static files
served by `Veyra.Api` from the same container. The frontend uses only the public REST API.

## Alternatives considered

- Blazor Server: single language, but per-user server state and a SignalR connection per browser scale poorly for a
  monitoring UI that is left open on many screens; charting and virtualized-table libraries are limited.
- Blazor WebAssembly: single language, but large initial download and the same library gap.
- Vue or Svelte: comparable to React; React chosen for the largest pool of observability-oriented components
  (virtualized grids, uPlot/ECharts bindings) and contributors.

## Consequences

- Two toolchains (.NET SDK and Node) are needed to build from source; the `Dockerfile` handles both in one multi-stage
  build.
- The REST API must be complete enough for the UI, which keeps it usable for automation too.
- ESLint, Prettier, and TypeScript strictness are part of the definition of done.
