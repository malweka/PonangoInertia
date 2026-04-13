# Testing And Documentation Requirements

This document defines the minimum maintenance expectations for tests and docs.

## Tests

The repository should keep an executable test suite under:

- `tests/Ponango.Inertia.Tests/`

Coverage should include:
- middleware behavior
- asset version mismatch behavior
- partial reload filtering
- prop wrapper semantics
- error bags
- precognition
- flash merging
- external redirects
- prefetch handling
- infinite scroll metadata
- public API ergonomics

When protocol behavior changes, corresponding tests should be added or updated in the same change.

## Documentation

The repository should keep:
- `README.md` as the entry point
- `docs/getting-started.md` for new users
- `docs/advanced-topics.md` for data and protocol behavior
- `docs/migration-from-v1.md` for upgrade guidance
- `CHANGELOG.md` for released changes

## Agent guidance

Agent-facing docs should:
- direct readers to `README.md` first
- avoid depending on ephemeral planning notes
- reference stable requirement documents instead
