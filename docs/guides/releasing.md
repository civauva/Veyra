# Releasing

How a version of Veyra is numbered, described, and shipped (ADR 0030).

## Numbering

- Semantic Versioning: `major.minor.patch`. While Veyra is `0.x`, a minor version adds features and a patch fixes.
- The version lives in one place, `<Version>` in `Directory.Build.props`. The frontend shows what the server answers.
- Each release is tagged `v<version>` in git, for example `v0.4.1`.

## Release notes

`CHANGELOG.md` at the repository root holds the notes. The app reads them from the server, behind the version in the
sidebar.

- Write each change under `## [Unreleased]` as it lands on `main`, under `### Added`, `### Changed`, `### Fixed`, or
  `### Removed`.
- Write for the people who use Veyra. Say what they can do now or what works better, not which files changed.
- One bullet a change. A bullet may run over several lines, indented under the first.
- An optional sentence under the version heading sums up the release.

## Cutting a release

1. Rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` and add an empty `## [Unreleased]` above it.
2. Set `<Version>x.y.z</Version>` in `Directory.Build.props`.
3. Run the tests. `ReleaseNotesTests.Embedded_NewestRelease_IsTheVersionThisBuildCarries` fails when the two differ.
4. Commit, merge, and tag `vx.y.z`.
5. Deploy (see [`deployment.md`](deployment.md)). Open pages show the notice of the new version within five minutes,
   or as soon as they are shown again.
