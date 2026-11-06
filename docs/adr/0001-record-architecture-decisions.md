# 0001. Record architecture decisions

Date: 2026-11-07
Status: Accepted

## Context

Veyra is starting from an empty repository with several foundational choices already discussed outside the codebase.
Contributors and automated agents need a place to find why a choice was made without reading chat history or git logs.

## Decision

Record every hard-to-reverse decision as an Architecture Decision Record in `docs/adr/`, using the template in
`docs/adr/README.md`. ADRs are numbered sequentially, immutable after acceptance, and superseded rather than edited.

## Alternatives considered

- Wiki pages: not versioned with the code, drift from reality.
- Comments in `README.md`: becomes unreadable as the number of decisions grows.
- No records: the default, and the reason this ADR exists.

## Consequences

- Every significant choice has a single, discoverable explanation.
- Small overhead per decision. Trivial choices do not need an ADR.
- `AGENTS.md` section 2 must list the current accepted decisions and link here.
