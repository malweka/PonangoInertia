# Public API Requirements

This document captures the intended developer-facing API for `Ponango.Inertia`.

## Primary rendering APIs

Preferred APIs:
- `InertiaContext.Render(...)`
- `InertiaController.Render(...)`

Compatibility APIs:
- `InertiaContext.Inertia(...)`
- `InertiaController.Inertia(...)`

Compatibility APIs should remain marked obsolete while they are still supported.

## Response builder

`InertiaResult` should support fluent composition through:
- `With(string key, object value)`
- `WithFlash(string key, object value)`
- `WithErrors(...)`
- `WithEncryptHistory(...)`
- `WithClearHistory(...)`
- `WithPreserveFragment(...)`
- `WithPreserveBigIntegers(...)`

## Static factory helpers

The `Inertia` static class should expose helper constructors for:
- `Optional`
- `Always`
- `Defer` (with optional `rescue`)
- `Merge`
- `DeepMerge`
- `Once`
- `Scroll`

Prop wrappers should be composable through fluent modifiers:
- `DeferredProp`: `Merge`, `DeepMerge`, `Append`, `Prepend`, `MatchingOn`, `Once`, `As`, `Fresh`, `Until`, `Rescue`
- `MergeProp`: `Append`, `Prepend`, `DeepMerge`, `MatchingOn`, `Once`, `As`, `Fresh`, `Until`
- `OptionalProp`: `Once`, `As`, `Fresh`, `Until`
- `OnceProp`: `As`, `Fresh`, `Until`

`InertiaContext` should expose `Share(...)` and `ShareOnce(...)`.

A prop value that is a delegate taking no arguments and returning a value should be evaluated lazily, whatever
its return type.

The interfaces the resolver uses to describe prop behavior (`IResolvableProp` and the other capability
interfaces) are internal. Prop types defined outside the library are not a supported extension point.

## Controller conveniences

`InertiaController` should expose:
- `Render(...)`
- `Location(...)`
- request-state helpers:
  - `IsInertia`
  - `IsPrefetch`
  - `IsPrecognition`

Default component derivation should support route-based naming, including area/controller/action when available.

## Setup APIs

`ServiceCollectionExtensions` should support:
- `AddInertia(Action<JsonSerializerOptions>?)`
- `AddInertia(Action<InertiaOptions>?)`

`IApplicationBuilder` should support:
- `UseInertia()`

## Configuration

`InertiaOptions` should support:
- `RootView`
- `EncryptHistory`
- `PreserveBigIntegers`
- `WithAllErrors`
- `ExposeSharedPropKeys`
- `SharedData`
- `JsonSerializerOptions` (applied once; the resulting options are reused for every response)

## Location responses

There should be a first-class API for external location responses:
- `InertiaContext.Location(...)`
- `InertiaController.Location(...)`

## Documentation expectations

Public docs should describe:
- preferred APIs
- middleware setup
- migration guidance
- advanced data-management patterns
