# Frontend

React + TypeScript single-page application built with Vite (ADR 0004). The production build is a set of static files
that `Veyra.Api` serves from the same container; the app talks to the backend only through the REST API
([`docs/api/rest.md`](../../docs/api/rest.md)).

## Screens

| Path | Who | What |
| --- | --- | --- |
| `/setup?token=…` | anyone, until the first administrator exists | First-run setup with the link from Veyra's log |
| sign-in | anyone | Local sign-in; shown whenever there is no session |
| `/logs` | Viewer | Log Explorer: filters, message search, histogram, result list, the selected record |
| `/keys` | Admin | Ingestion keys: create (the secret is shown once), list, revoke |

The Log Explorer keeps its filters in the URL, so a link or a reload shows the same view. "Follow new" asks for new
records every 5 seconds. "± 1 minute, this service" switches to a fixed window around the selected record. Times are
shown in UTC everywhere.

Every screen works on desktop, tablet, and phone, in light and dark themes, and with the keyboard. In the Log
Explorer the arrow keys move the selection and Escape closes the record or the filter sheet.

## Layout

```
src/
  api/        REST client and the API's types
  app/        app frame: routes, top bar, theme toggle
  auth/       sign-in and first-run setup
  keys/       ingestion keys screen
  logs/       Log Explorer: filters, histogram, list, detail, and the search hook
  session/    who is signed in, shared through React context
  styles/     design tokens for both themes, base styles
  test/       Vitest setup
e2e/          Playwright tests, run at desktop, tablet, and phone sizes
```

Components, styles, and tests sit side by side. Colours come only from `styles/tokens.css`.

## Commands

Run from this folder.

```bash
npm ci
```

```bash
npm run dev
```

```bash
npm test
```

```bash
npm run test:e2e
```

```bash
npm run lint
```

```bash
npm run format:check
```

```bash
npm run build
```

- `npm run dev` serves the app on port 5173 and forwards `/api` to the API on its development port 5292
  (`dotnet run --project src/backend/Veyra.Api`).
- `npm test` runs the unit and component tests (Vitest, Testing Library, jsdom).
- `npm run test:e2e` builds the app and runs it in Chromium at three viewport sizes. The tests answer the REST API
  themselves, so no backend is needed. Run `npx playwright install chromium` once first.
- `npm run build` type-checks and writes `dist/`. The Docker image builds the app in its own stage and copies
  `dist/` into the API's `wwwroot`.
