# Scripts

Cross-platform helper scripts (PowerShell and bash variants where needed): build, test, database reset, local stack up/down.
Scripts must be runnable from the repository root and must not depend on a developer's machine state.

| Folder | Purpose |
| --- | --- |
| `deploy.sh` | Builds a branch or tag here and deploys it over SSH to a server behind a shared proxy |
| [benchmark](benchmark/README.md) | Seeds the reference volume and checks every request against its budget (ADR 0015) |
