# AGENTS.md

This file provides guidance to coding agents working in this repository.

## Read First

All agents must read `README.md` before making changes.

It is the current public-facing source of truth for:
- package setup
- middleware requirements
- preferred rendering APIs
- docs entry points
- upgrade notes

For implementation work, also consult:
- `docs/getting-started.md`
- `docs/advanced-topics.md`
- `docs/migration-from-v1.md`
- `docs/upgrading-to-3.0.md`
- `requirements/protocol-requirements.md`
- `requirements/public-api-requirements.md`
- `requirements/testing-and-docs-requirements.md`
- `CHANGELOG.md`

## Project Overview

Ponango.Inertia is a .NET 8.0 ASP.NET Core server adapter for Inertia.js with a v3-oriented protocol implementation.

Current capabilities include:
- v3-style initial HTML payload via `<script type="application/json" data-page data-inertia>`
- middleware-driven protocol handling via `app.UseInertia()`
- shared props, once-shared props, and flash data emitted as the top-level `page.flash` field
- error bags (optionally every message per field) and precognition
- history flags: `encryptHistory`, `clearHistory`, `preserveFragment`
- prop wrappers, composable through fluent modifiers:
  - `OptionalProp`
  - `AlwaysProp`
  - `DeferredProp` (with merge, once, and rescue)
  - `MergeProp` (root or nested-path merging, once)
  - `OnceProp`
  - `ScrollProp` (infinite scroll)
- lazy delegate props (`Func<object>`)
- nested prop types and dot-notation partial reloads
- big integer markers (`preserveBigIntegers`)
- prefetch detection
- ergonomic APIs such as `Render(...)`, `Location(...)`, `With(...)`, and `WithFlash(...)`

## Build and Test Commands

Use `-p:`-style switches: Git Bash rewrites `/p:` switches as paths and MSBuild then fails with
`MSB1008: Only one project can be specified`. The `-` form works in every shell.

```bash
# Build the library without package generation
dotnet build src/Ponango.Inertia/Ponango.Inertia.csproj -p:GeneratePackageOnBuild=false -nr:false -v:minimal

# Run the test project
dotnet test tests/Ponango.Inertia.Tests/Ponango.Inertia.Tests.csproj -p:GeneratePackageOnBuild=false -nr:false -v:minimal

# Pack the NuGet package
dotnet pack src/Ponango.Inertia/Ponango.Inertia.csproj -c Release
```

If you are only validating behavior, prefer the test command over ad hoc manual checking.

## Current Public API

### Preferred rendering API

Use `Render(...)` as the primary API.

```csharp
return _inertia.Render("Users/Index", new
{
    users,
    filters
});
```

### Legacy rendering API

The older `Inertia(...)` methods still exist for compatibility, but they are obsolete.

```csharp
return _inertia.Inertia("Inertia", model, "Users/Index");
```

### Fluent response APIs

`InertiaResult` supports:
- `With(string key, object value)`
- `WithFlash(string key, object value)` and `WithFlash(IDictionary<string, object?> values)`
- `WithErrors(ModelStateDictionary modelState, string? errorBag = null)`
- `WithEncryptHistory(bool encrypt = true)`
- `WithClearHistory(bool clear = true)`
- `WithPreserveFragment(bool preserve = true)`
- `WithPreserveBigIntegers(bool preserve = true)`

### Static factory helpers

Use the `Inertia` static class to create prop wrappers:

```csharp
Inertia.Optional(...)                        // .Once() .As() .Fresh() .Until()
Inertia.Always(...)
Inertia.Defer(..., group, rescue)            // .Merge() .DeepMerge() .Append() .Prepend() .MatchingOn() .Once() .Rescue()
Inertia.Merge(...)  / Inertia.DeepMerge(...) // .Append(path, matchOn) .Prepend(path) .MatchingOn() .Once()
Inertia.Once(...)                            // .As() .Fresh() .Until()
Inertia.Scroll(..., ScrollMetadata, wrapper) // .Defer() .MatchingOn()
```

`InertiaContext.ShareOnce(...)` shares a once prop. A `Func<object>` prop value is evaluated lazily.

### Controller base class

`InertiaController` exposes:
- `Render(...)`
- `Location(...)`
- `Redirect(...)` (303 for non-GET requests)
- obsolete `Inertia(...)`
- request flags:
  - `IsInertia`
  - `IsPrefetch`
  - `IsPrecognition`

## Required Runtime Wiring

Applications should:

1. Register services with `AddInertia(...)`
2. Register an `IAssetVersionProvider`
3. Add `app.UseInertia()` to the middleware pipeline

`UseInertia()` is important for:
- `Vary: X-Inertia`
- asset version mismatch handling
- shared data middleware behavior
- external and fragment redirect handling

## Core Architecture

### InertiaMiddleware

`src/Ponango.Inertia/InertiaMiddleware.cs`

Responsible for:
- appending `X-Inertia` to `Vary`
- asset version mismatch handling (`409` + `X-Inertia-Location` + `X-Inertia-Version`)
- shared data injection from `InertiaOptions.SharedData`
- external redirect translation to `409` + `X-Inertia-Location`
- internal redirects to a `#fragment` URL translated to `409` + `X-Inertia-Redirect` (not for prefetch)
- `302` to `303` conversion for non-GET Inertia redirects

### InertiaResult

`src/Ponango.Inertia/InertiaResult.cs`

Responsible for:
- merging shared and page props (shared first, page props win), adding the default `errors` object
- running `PropsResolver` and copying its metadata onto the page object
- HTML vs JSON response generation
- history/navigation flags and the `preserveBigIntegers` flag
- reading flash data into `page.flash` when a page is built
- protocol fallback if middleware is missing

### PropsResolver

`src/Ponango.Inertia/PropsResolver.cs`

A port of the reference adapter's `PropsResolver` (inertia-laravel 3.x). Responsible for:
- partial reload filtering with dot-path matching (`only` / `except`)
- excluding optional/deferred props and client-held once props on full visits, while collecting their metadata
- resolving wrappers and lazy delegates, rescuing rescuable failures into `rescuedProps`
- collecting deferred, merge, match, scroll, and once metadata
- walking anonymous objects and string-keyed dictionaries (`PropContainers`) for nested prop types

Prop behavior is described by the public capability interfaces in `PropCapabilities.cs` (`IResolvableProp`,
`IIgnoreFirstLoad`, `IDeferrableProp`, `IMergeableProp`, `IOnceableProp`, `IRescuableProp`). When adapting more
reference-adapter behavior, read `src/PropsResolver.php` in inertia-laravel first and follow it.

### InertiaContext

`src/Ponango.Inertia/InertiaContext.cs`

Provides:
- request header access
- shared props (`Share`, `ShareOnce`)
- flash support
- request flags:
  - `IsInertia`
  - `IsPrefetch`
  - `IsPrecognition`

### PageModel

`src/Ponango.Inertia/PageModel.cs`

Includes:
- core page payload
- merge metadata
- deferred and rescued metadata
- once metadata
- shared prop metadata
- scroll metadata
- history/navigation flags
- `flash` and `preserveBigIntegers`

## Prop Wrapper Semantics

### OptionalProp

- never resolved on a full visit
- on a partial reload, resolved when it passes the `only`/`except` filters

### AlwaysProp

- always emitted, including partial reloads, ignoring `only`/`except`

### DeferredProp

- excluded from the initial full visit and listed under `deferredProps` (unless it is a once prop the client holds)
- resolved by a later partial reload that selects it
- with rescue, a failing callback is logged, omitted, and listed in `rescuedProps`
- can also merge and be remembered (once)

### OnceProp

- skipped (callback not run) on Inertia full visits when its key is in `X-Inertia-Except-Once-Props`, but its
  `onceProps` entry is still emitted
- always resolved on partial reloads that select it
- `expiresAt` is in Unix milliseconds; `As(key)` shares a cache key across pages; `Fresh()` forces a new value

### MergeProp

- emits merge metadata (append, prepend, deep merge) at the root or at nested paths (`prop.path`)
- supports match fields (`matchPropsOn`)
- legacy `WithScroll(...)` (obsolete) adds scroll metadata and follows the merge-intent header

### ScrollProp

- merges the array under its wrapper key (`prop.data`), prepending when the merge-intent header says `prepend`
- emits `scrollProps` with `reset`; when deferred, emits no `scrollProps` on the full visit

## Request/Protocol Notes

Supported request headers include:
- `X-Inertia`
- `X-Inertia-Version`
- `X-Inertia-Partial-Component`
- `X-Inertia-Partial-Data`
- `X-Inertia-Partial-Except`
- `X-Inertia-Reset`
- `X-Inertia-Error-Bag`
- `X-Inertia-Except-Once-Props`
- `X-Inertia-Infinite-Scroll-Merge-Intent`
- `Purpose`
- `Precognition`
- `Precognition-Validate-Only`

Important behavior:
- `props.errors` is always present (`{}` by default) and survives partial reloads
- with both `only` and `except`, `only` narrows first and `except` is then removed
- `OptionalProp`, `LazyProp`, and `DeferredProp` never resolve on full visits
- `X-Inertia-Reset` suppresses merge metadata for matching props and sets `scrollProps[*].reset`
- `X-Inertia-Infinite-Scroll-Merge-Intent` only affects scroll props
- precognitive actions add `Precognition` to `Vary`
- the initial-page script payload escapes `/` as `\/` and `<` as `\u003c`

## View Integration

In Razor layouts:

```cshtml
@inject IJsonSerializerOptionBuilder Serializer
<body>
    @Html.InertiaRender(Serializer, appId: "app")
</body>
```

This emits:

```html
<div id="app"></div>
<script type="application/json" data-page="app" data-inertia>{...}</script>
```

## Tests

There is an active test project at:

- `tests/Ponango.Inertia.Tests/`

Current coverage includes:
- middleware behavior and protocol compliance (`ProtocolComplianceTests`)
- prop resolution and metadata (`PropsResolverTests`, `NestedPropsTests`)
- infinite scroll (`ScrollPropTests`)
- flash data at page level (`FlashTests`)
- big integers (`BigIntegerTests`)
- options and conveniences (`OptionsAndConveniencesTests`)
- external and fragment redirects
- prefetch detection
- precognition behavior
- public API ergonomics

Shared test helpers live in `TestInfrastructure.cs` and `TestHelpers.cs`.

When changing protocol behavior, add or update tests in `tests/Ponango.Inertia.Tests/` in the same change.

## Implementation Notes

- Prefer `Render(...)` in new examples and code.
- Keep compatibility APIs unless the change explicitly intends a breaking removal.
- Do not regress middleware-backed behavior for apps that already call `UseInertia()`.
- If changing request/response semantics, update `README.md`, the docs under `docs/`, and `requirements/`.
