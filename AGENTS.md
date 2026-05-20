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
- `requirements/protocol-requirements.md`
- `requirements/public-api-requirements.md`
- `requirements/testing-and-docs-requirements.md`
- `CHANGELOG.md`

## Project Overview

Ponango.Inertia is a .NET 8.0 ASP.NET Core server adapter for Inertia.js with a v3-oriented protocol implementation.

Current capabilities include:
- v3-style initial HTML payload via `<script type="application/json" data-page data-inertia>`
- middleware-driven protocol handling via `app.UseInertia()`
- shared props and flash messages
- error bags and precognition
- history flags: `encryptHistory`, `clearHistory`, `preserveFragment`
- prop wrappers:
  - `OptionalProp`
  - `AlwaysProp`
  - `DeferredProp`
  - `MergeProp`
  - `OnceProp`
- prefetch detection
- infinite scroll metadata via `scrollProps`
- ergonomic APIs such as `Render(...)`, `Location(...)`, `With(...)`, and `WithFlash(...)`

## Build and Test Commands

```bash
# Build the library without package generation
dotnet msbuild src/Ponango.Inertia/Ponango.Inertia.csproj /t:Build /p:GeneratePackageOnBuild=false /p:BuildProjectReferences=false /nr:false /v:minimal

# Run the test project
dotnet test tests/Ponango.Inertia.Tests/Ponango.Inertia.Tests.csproj /p:GeneratePackageOnBuild=false /nr:false /v:minimal

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
- `WithFlash(string key, object value)`
- `WithErrors(ModelStateDictionary modelState, string? errorBag = null)`
- `WithEncryptHistory(bool encrypt = true)`
- `WithClearHistory(bool clear = true)`
- `WithPreserveFragment(bool preserve = true)`

### Static factory helpers

Use the `Inertia` static class to create prop wrappers:

```csharp
Inertia.Optional(...)
Inertia.Always(...)
Inertia.Defer(...)
Inertia.Merge(...)
Inertia.Once(...)
```

### Controller base class

`InertiaController` exposes:
- `Render(...)`
- `Location(...)`
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
- external redirect handling

## Core Architecture

### InertiaMiddleware

`src/Ponango.Inertia/InertiaMiddleware.cs`

Responsible for:
- appending `X-Inertia` to `Vary`
- asset version mismatch handling
- shared data injection from `InertiaOptions.SharedData`
- external redirect translation to `409` + `X-Inertia-Location` or `X-Inertia-Redirect`
- `302` to `303` conversion for non-GET Inertia redirects

### InertiaResult

`src/Ponango.Inertia/InertiaResult.cs`

Responsible for:
- building the page object
- resolving prop wrappers
- HTML vs JSON response generation
- history/navigation flags
- flash merge before response generation
- protocol fallback if middleware is missing

### InertiaContext

`src/Ponango.Inertia/InertiaContext.cs`

Provides:
- request header access
- shared props
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
- deferred metadata
- once metadata
- shared prop metadata
- scroll metadata
- history/navigation flags

## Prop Wrapper Semantics

### OptionalProp

- excluded from full visits
- only resolved when explicitly requested in `X-Inertia-Partial-Data`

### AlwaysProp

- always emitted, including partial reloads

### DeferredProp

- excluded from the initial full visit
- listed under `deferredProps`
- only resolved when explicitly requested later

### OnceProp

- emitted once unless excluded by `X-Inertia-Except-Once-Props`

### MergeProp

- emits merge metadata
- supports append, prepend, deep merge
- supports `matchOn`
- supports infinite scroll metadata via `WithScroll(...)`

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
- partial reloads preserve `errors`
- `OptionalProp`, `LazyProp`, and `DeferredProp` do not resolve unless explicitly requested
- `X-Inertia-Reset` suppresses merge metadata for matching props
- `X-Inertia-Infinite-Scroll-Merge-Intent` can override append/prepend merge mode

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
<script type="application/json" data-page data-inertia>{...}</script>
```

## Tests

There is an active test project at:

- `tests/Ponango.Inertia.Tests/`

Current coverage includes:
- middleware behavior
- prop wrapper resolution
- flash merging
- external redirects
- prefetch detection
- precognition behavior
- public API ergonomics

When changing protocol behavior, add or update tests in `tests/Ponango.Inertia.Tests/` in the same change.

## Implementation Notes

- Prefer `Render(...)` in new examples and code.
- Keep compatibility APIs unless the change explicitly intends a breaking removal.
- Do not regress middleware-backed behavior for apps that already call `UseInertia()`.
- If changing request/response semantics, update `README.md` and the docs under `docs/`.
