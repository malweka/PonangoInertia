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
- flash data emitted at page level (`page.flash`)
- external redirects
- prefetch handling
- infinite scroll metadata
- composed prop types (deferred/merge/once/rescue) and nested dot-notation paths
- big integer markers
- public API ergonomics

When protocol behavior changes, corresponding tests should be added or updated in the same change.

## Documentation

The repository should keep:
- `README.md` as the entry point
- `docs/getting-started.md` for new users
- `docs/advanced-topics.md` for data and protocol behavior
- `docs/compatibility.md` for feature-by-feature compatibility with the current Inertia.js release
- `docs/migration-from-v1.md` and `docs/upgrading-to-3.0.md` for upgrade guidance
- `CHANGELOG.md` for released changes

## Agent guidance

Agent-facing docs should:
- direct readers to `README.md` first
- avoid depending on ephemeral planning notes
- reference stable requirement documents instead
