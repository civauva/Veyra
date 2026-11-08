# Methodology

How work moves from an idea to a release.

## Planning

- **Work items.** Planning and tracking happen in Throughly, in project VEY. Every change starts as a work item.
- **Visuals.** A work item carries a mockup or a diagram wherever a picture explains it faster than text.
- **Plans.** A longer piece of work, by a person or an AI agent, gets a plan in Throughly on its work item. The
  plan lists the steps with estimates, and each step is reported as it starts and ends, so progress is visible.
- **Decisions.** A choice that is hard to reverse gets an ADR in `docs/adr/` before the code is written.
- **Contributors.** Outside contributors are welcome to open GitHub issues and pull requests. The maintainer links
  them to work items.

## Building

- **Thin slices.** Build one thin path that works from start to finish, then widen it. For example, one log record
  travels from a .NET app through ingestion and storage to the Log Explorer before search gets richer.
- **Test-driven development.** All production code is written test first, in small red, green, refactor steps:
  write a failing test, write the simplest code that makes it pass, then clean up with the tests still green. A bug
  fix starts with a failing test that reproduces the bug. Only throwaway spikes and pure scaffolding or
  configuration are exempt.
- **Test levels.** Unit tests have no I/O. Integration tests use Testcontainers for PostgreSQL. A few end-to-end
  tests cover the main paths.
- **Readable code.** Code follows [`coding-standards.md`](coding-standards.md), including XML and TSDoc comments.

## Delivering

- **Branches.** One branch per work item, named `feature/<short-name>`, `fix/<short-name>`, or
  `docs/<short-name>`, branched from `main`.
- **Commit titles.** Every commit names its work item: `#<work item key>: type(scope): summary`, for example
  `#VEY-42: feat(ingestion): accept OTLP logs over HTTP`.
- **Pull requests.** Small and focused. The title is the commit title, because it becomes the commit.
- **Squash merge.** Every pull request is squash-merged, so `main` has one commit per change.
- **Audit.** Before a work item is marked done, the change is audited against this checklist, and the result is
  posted as a short comment on the work item:
  - Tests were written first, new behaviour is covered, and all tests pass.
  - The build has zero warnings, and the format check passes.
  - The code meets [`coding-standards.md`](coding-standards.md), including comments and XML or TSDoc.
  - No secrets, input is validated, SQL is parameterised, and authorisation is checked.
  - UI changes work on desktop, tablet, and phone, in light and dark themes, and with the keyboard.
  - Documentation and ADRs are updated.

  A finding is fixed before closing, or filed as a new work item.
- **Done.** A change is done when it meets the definition of done in [`AGENTS.md`](../../AGENTS.md), including
  documentation.

## Releasing

- **Versions.** Semantic versioning. Pre-release milestones are `v0.x`, as listed in the roadmap.
- **Release notes.** Each version has release notes that describe what changed for users.
- **Reference installation.** Merged changes are deployed often to the maintainer's reference installation, which
  Veyra also monitors. Problems found there become work items.
