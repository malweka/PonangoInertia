# Public API Requirements

This document captures the intended developer-facing API for `Ponango.Inertia`.

## Primary rendering APIs

Preferred APIs:
- `InertiaContext.Render(...)`
- `InertiaController.Render(...)`

Compatibility APIs:
- `InertiaContext.Inertia(...)`

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
- `Live` (value, events and channels, or an `ILiveEvent`, with an optional throttle)
- `BroadcastPropsAsync` (the `{ "__inertia": { "props": ... } }` payload of a broadcast event)

Prop wrappers should be composable through fluent modifiers:
- `DeferredProp`: `Merge`, `DeepMerge`, `Append`, `Prepend`, `MatchingOn`, `Once`, `As`, `Fresh`, `Until`, `Rescue`
- `MergeProp`: `Append`, `Prepend`, `DeepMerge`, `MatchingOn`, `Once`, `As`, `Fresh`, `Until`
- `OptionalProp`: `Once`, `As`, `Fresh`, `Until`
- `OnceProp`: `As`, `Fresh`, `Until`
- `Live` on `LiveProp`, `OptionalProp`, `DeferredProp`, `OnceProp` and `AlwaysProp` (shared `LiveModifiers<TSelf>`);
  it throws on a merging prop, and merge modifiers throw on a live prop

`LiveChannel` (name and `LiveChannelType`, a string converts to a public channel) and `ILiveEvent` describe what a
live prop listens to. Its constructor removes a prefix matching the type once; `LiveChannel.TryParse(type, name)`
reads a channel from the wire without removing anything, so a name round-trips.

`InertiaContext` should expose `Share(...)` and `ShareOnce(...)`, and `FlashErrors(ModelStateDictionary, bag)` /
`FlashErrors(IDictionary<string, string>, bag)` to keep validation errors for the next rendered page.

A prop value that is a delegate taking no arguments and returning a value should be evaluated lazily, whatever
its return type.

The interfaces the resolver uses to describe prop behavior (`IResolvableProp` and the other capability
interfaces) are internal. Prop types defined outside the library are not a supported extension point.

## Controller conveniences

`InertiaController` should expose:
- `Render(...)`
- `Location(...)`
- `Back(...)`
- request-state helpers:
  - `IsInertia`
  - `IsPrefetch`
  - `IsPrecognition`
  - `SocketId`

`InertiaController` must not declare a member named `Inertia`, which would hide the static `Inertia` factory class in
derived controllers.

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
- `PersistValidationErrorsOnRedirect`
- `RedirectEmptyResponsesBack`
- `ExposeSharedPropKeys`
- `SharedData`
- `JsonSerializerOptions` (applied once; the resulting options are reused for every response)

## SignalR package

`Ponango.Inertia.SignalR` should provide:
- `AddInertiaLive(options => options.Channel(pattern, rule))` and `MapInertiaLive(path = "/inertia/live")`
- `InertiaLiveHub` with `Subscribe(type, name)` / `Unsubscribe(type, name)`; public channels are open, private and
  presence channels need a matching rule, encrypted private channels are refused
- `IInertiaBroadcaster.BroadcastAsync(...)` for an `ILiveEvent` or an event name and channels, with optional props
  and an excluded socket id; it sends `inertia(channelKey, payload)` to the group `{type}:{name}`
- the protocol names as constants in `InertiaLiveProtocol`, kept in sync with the `@ponango/inertia-signalr`
  client transport

## Location responses

There should be a first-class API for external location responses:
- `InertiaContext.Location(...)`
- `InertiaController.Location(...)`

## Redirect back

`InertiaContext.Back(fallbackUrl)` and `InertiaController.Back(fallbackUrl)` should redirect to the `Referer` when
it is on the request's host and its path and query form a local path, else to the fallback URL. The fallback must be
a local path (an `ArgumentException` otherwise). They must never redirect to another host, including through a
same-host referer whose path starts with `//` or `/\`.

## Documentation expectations

Public docs should describe:
- preferred APIs
- middleware setup
- migration guidance
- advanced data-management patterns
