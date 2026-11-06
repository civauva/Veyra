# 0005. License: Apache 2.0

Date: 2026-11-07
Status: Accepted

## Context

Veyra is intended to be open source and to integrate closely with OpenTelemetry. The README left the license undecided.

## Decision

License the project under the Apache License, Version 2.0. The `LICENSE` file holds the full text and `NOTICE` holds
the attribution line.

## Alternatives considered

- MIT: equally permissive and shorter, but has no explicit patent grant.
- AGPL 3.0: protects against closed SaaS forks, but discourages adoption inside companies, which is the main audience.
- Business Source or similar: not open source by the common definition.

## Consequences

- Same license as OpenTelemetry and most of the ecosystem Veyra integrates with, so no compatibility questions.
- Contributions are accepted under the same license (Apache 2.0 section 5); no separate CLA needed for now.
