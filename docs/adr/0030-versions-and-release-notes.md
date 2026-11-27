# 0030. Versions, release notes in the app, and the notice of a newer version

Date: 2026-11-27
Status: Accepted

## Context

Veyra had a version in `Directory.Build.props` (0.2.1), shown only on Settings → System. People could not tell which
version they were using, nor what a release had changed. A page left open across a deploy kept running the old app
against the new API, with nothing to say so.

The maintainer asked for three things (VEY-82):

- the version in the app, which opens the release notes;
- a notice with Reload when a newer version runs;
- numbering from 0.4.1.

## Decision

### 1. One version, one changelog

- **The number.** `Directory.Build.props` holds the version for every project, in Semantic Versioning. It starts at
  0.4.1. The frontend has no version of its own: it shows what the server answers.
- **The notes.** `CHANGELOG.md` at the repository root holds the release notes in the Keep a Changelog format:
  - `## [version] - date` for each release, newest first, with an optional sentence under it;
  - `### Added`, `### Changed`, `### Fixed`, or `### Removed`, one bullet a change, in words for the people who use
    Veyra;
  - `## [Unreleased]` collects changes on `main` until the next release.
- **The check.** The Access module embeds the file. A unit test fails when the newest release in it is not the
  version the build carries, so no version ships without its notes.

### 2. The API

Both reads are for anyone signed in (Viewer), so the version is not told to anyone who is not.

- **`GET /api/v1/about`** answers `{ "version": "0.4.1", "build": "3f9a1c0b2d4e" }`.
  - `build` is the first twelve hex digits of the SHA-256 of the served `index.html`.
  - Vite names every bundle by its content, so any change to the app changes `index.html`, and so `build`.
  - Without a built app, as in development, `build` is the version.
- **`GET /api/v1/about/releases`** answers the parsed notes, newest first:
  `[{ "version", "date", "summary", "sections": [{ "title", "items" }] }]`. Unreleased is left out.

### 3. The app

- **The version.** It sits at the foot of the sidebar in every look, where "Self-hosted" was. It opens the release
  notes in a dialog, with the version the page runs marked.
- **The notice.** The shell reads `/api/v1/about` when the page loads, every five minutes, and whenever the page is
  shown again.
  - The first answer is the build the page loaded.
  - A different `build` later means a newer app runs.
  - The notice in the corner then names the new version and offers What's new, Later, and Reload.
  - It takes no focus, so nobody loses what they are typing. Later hides it until another build.
- **Failures.** A failed read changes nothing; knowing the version never takes a screen down.

## Alternatives considered

- **Bake the version into the frontend bundle.** Rejected: the page would know its own version without asking, but
  could still only learn of a newer one by asking the server. The served `index.html` already identifies the app.
- **Compare versions instead of builds.** Rejected: a fix deployed without a new number would offer nothing, though the
  page runs stale code. The build catches every change to the app; the notice words a same-version build as "a new
  build".
- **A service worker that reloads on its own.** Rejected: reloading under someone's hands loses what they typed, and a
  service worker adds caching rules Veyra does not otherwise need.
- **Release notes from Throughly or GitHub releases.** Rejected: the app would depend on a service outside the
  installation, and self-hosted installations may have no internet access.

## Consequences

- Each release updates two files: the version in `Directory.Build.props` and its section in `CHANGELOG.md`. The test
  keeps them in step. `docs/guides/releasing.md` says how.
- `deploy/Dockerfile` copies `CHANGELOG.md` into the build.
- Settings → System shows the same version, without the SDK's build suffix.
- A deploy that changes only the backend offers no reload. None is needed: the app the page runs did not change.
