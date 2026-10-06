# Protocol Requirements

This document captures the protocol-level requirements for `Ponango.Inertia`.

It is intended to remain stable even if planning notes are removed.

## Core response model

The adapter must emit an Inertia page object with:
- `component`
- `url`
- `version`
- `props` (always containing an `errors` object, `{}` when there are no errors)

Optional page object fields must be emitted only when applicable:
- `encryptHistory`
- `clearHistory`
- `preserveFragment`
- `mergeProps`
- `prependProps`
- `deepMergeProps`
- `matchPropsOn`
- `scrollProps`
- `deferredProps`
- `rescuedProps`
- `sharedProps`
- `onceProps`
- `flash` (only when flash data exists; never inside `props`)
- `preserveBigIntegers` (only when big integer support is enabled for the response)

When big integer support is enabled, integers outside ±(2^53 - 1) in props and flash must be written as
`{"$bigint": "<digits>"}` markers.

## Initial HTML response

The initial non-Inertia response must render:

```html
<div id="app"></div>
<script type="application/json" data-page data-inertia>{...}</script>
```

The legacy `data-page` attribute format is not the target format.

The JSON inside the script tag must escape every `/` as `\/` (and `<` as `\u003c`) so no sequence in prop
data can close the script element early. HTML-entity encoding must not be used.

## Middleware behavior

Middleware support is required via `app.UseInertia()`.

The middleware layer must:
- append `X-Inertia` to `Vary`
- enforce asset version mismatch handling
- inject configured shared data
- translate external redirects to `409` + `X-Inertia-Location`
- translate internal redirects whose target contains a `#fragment` (non-prefetch) to `409` + `X-Inertia-Redirect`
- convert non-GET Inertia `302` redirects to `303`

## Asset version handling

For Inertia GET requests:
- compare `X-Inertia-Version` to the current asset version
- on mismatch, return `409`
- include `X-Inertia-Location` with the current request URL
- include `X-Inertia-Version` with the current asset version
- leave pending flash data unread so it survives the follow-up request

## Shared props

Shared props may be provided through:
- `InertiaContext.Share(...)`
- `InertiaOptions.SharedData`

Flash data is not a shared prop: it is emitted in the top-level `flash` page field.

The page object’s `sharedProps` metadata must reflect the keys that were actually emitted.

## Partial reload behavior

Supported partial reload headers:
- `X-Inertia-Partial-Component`
- `X-Inertia-Partial-Data`
- `X-Inertia-Partial-Except`
- `X-Inertia-Reset`

Rules:
- partial paths may use dot notation: a path selects that prop, its descendants and its ancestors; prop types
  nested in anonymous objects and string-keyed dictionaries are resolved and reported with dot paths
- `errors` must always be preserved
- `AlwaysProp` must always be preserved
- when both data and except lists are sent, the data list narrows first, then the except list is removed
- `OptionalProp`, obsolete `LazyProp`, and `DeferredProp` must never resolve on a full visit; on a partial
  reload they follow the same data/except filters as other props
- reset keys suppress merge metadata for matching props

## Prop wrapper requirements

### OptionalProp
- excluded from full visits
- only emitted when explicitly requested

### AlwaysProp
- always emitted

### DeferredProp
- omitted from the initial full visit
- represented in `deferredProps` (unless it is a once prop the client already remembers)
- emitted when selected by a later partial reload
- with rescue enabled, a failing callback omits the prop, logs the exception, and lists the key in `rescuedProps`
- may be combined with merge behavior and once behavior

### OnceProp
- on Inertia full visits, skipped (callback not run) when its key (custom `As` key or prop name) is listed in
  `X-Inertia-Except-Once-Props`, while its `onceProps` entry is still emitted
- always resolved on partial reloads that select it (`X-Inertia-Except-Once-Props` is ignored there)
- `Fresh()` forces a new value; `expiresAt` is a Unix timestamp in milliseconds or `null`
- once behavior may also be applied to optional, deferred, and merge props

### MergeProp
- contributes merge metadata (not for props listed in `X-Inertia-Reset`, nor for props excluded by a partial reload)
- supports append, prepend, and deep merge, at the root or at nested paths (`prop.path` labels)
- supports `matchOn` fields (`matchPropsOn` entries `prop.path.field`)
- supports legacy scroll metadata via `WithScroll(...)`
- `X-Inertia-Infinite-Scroll-Merge-Intent` only affects scroll props

## Request flags

`InertiaContext` must expose:
- `IsInertia`
- `IsPrefetch`
- `IsPrecognition`

Prefetch requests should use the normal response pipeline.

## Error bags

Validation errors must support:
- flat `errors`
- named error bags
- automatic use of `X-Inertia-Error-Bag`

## Precognition

Precognition handling must:
- short-circuit before controller action side effects
- return `204` with `Precognition-Success: true` when valid
- return `422` with JSON errors when invalid
- honor `Precognition-Validate-Only`
- add `Precognition` to `Vary` on every response from a precognitive action

## History and navigation flags

The response API must support:
- `WithEncryptHistory(...)`
- `WithClearHistory(...)`
- `WithPreserveFragment(...)`

Global history encryption defaults must be configurable through `InertiaOptions`.

## External redirects

External navigation must support:
- `409` + `X-Inertia-Location` for `Location(...)` and for intercepted external redirects (with or without a
  fragment)
- `409` + `X-Inertia-Redirect` for intercepted internal redirects whose target contains a fragment, except on
  prefetch requests

## Infinite scroll

Infinite scroll support must include:
- `Inertia.Scroll(...)` / `ScrollProp`, merging the array under a wrapper key (`mergeProps: ["prop.data"]`)
- `scrollProps` entries with `pageName`, `previousPage`, `nextPage`, `currentPage` (numbers or cursor strings,
  `null` emitted explicitly) and `reset` (`true` when the prop is listed in `X-Inertia-Reset`)
- merge-intent override via `X-Inertia-Infinite-Scroll-Merge-Intent` (`prepend` switches to `prependProps`)
- optional deferral: a deferred scroll prop is announced in `deferredProps` and emits no `scrollProps` on the full
  visit
- the legacy `MergeProp.WithScroll(...)` API (obsolete), which merges at the root

## Compatibility requirements

The preferred public API is `Render(...)`.

Legacy `Inertia(...)` APIs may remain for compatibility, but they should be treated as deprecated rather than primary.
