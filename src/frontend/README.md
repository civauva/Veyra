# Frontend

React + TypeScript single-page application built with Vite.

- Production build outputs static files that `Veyra.Api` serves from the same container.
- Development runs the Vite dev server with a proxy to the API.
- Light and dark themes are required from the first screen; both must meet WCAG AA contrast.
- Every screen is responsive and works on desktop, tablet, and phone screens.
- Planned screens for v0.1: Overview, Log Explorer, Event/Incident Details. The Overview is delivered as the
  Forecast home dashboard; City follows as a second home. See `docs/architecture/home-dashboards.md`.
