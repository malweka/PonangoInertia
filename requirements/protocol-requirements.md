# Protocol Requirements

This document captures the protocol-level requirements for `Ponango.Inertia`.

It is intended to remain stable even if planning notes are removed.

## Core response model

The adapter must emit an Inertia page object with:
- `component`
- `url`
- `version`
- `props`

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
- `sharedProps`
- `onceProps`

## Initial HTML response

The initial non-Inertia response must render:

```html
<div id="app"></div>
<script type="application/json" data-page data-inertia>{...}</script>
```

The legacy `data-page` attribute format is not the target format.

## Middleware behavior

Middleware support is required via `app.UseInertia()`.

The middleware layer must:
- append `X-Inertia` to `Vary`
- enforce asset version mismatch handling
- inject configured shared data
- translate external redirects to `409`
- convert non-GET Inertia `302` redirects to `303`

## Asset version handling

For Inertia GET requests:
- compare `X-Inertia-Version` to the current asset version
- on mismatch, return `409`
- include `X-Inertia-Location` with the current request URL

## Shared props

Shared props may be provided through:
- `InertiaContext.Share(...)`
- `InertiaOptions.SharedData`
- flash message integration

The page object’s `sharedProps` metadata must reflect the keys that were actually emitted.

## Partial reload behavior

Supported partial reload headers:
- `X-Inertia-Partial-Component`
- `X-Inertia-Partial-Data`
- `X-Inertia-Partial-Except`
- `X-Inertia-Reset`

Rules:
- `errors` must always be preserved
- `AlwaysProp` must always be preserved
- `OptionalProp`, obsolete `LazyProp`, and `DeferredProp` must only resolve when explicitly requested
- reset keys suppress merge metadata for matching props

## Prop wrapper requirements

### OptionalProp
- excluded from full visits
- only emitted when explicitly requested

### AlwaysProp
- always emitted

### DeferredProp
- omitted from the initial full visit
- represented in `deferredProps`
- emitted when explicitly requested later

### OnceProp
- omitted when listed in `X-Inertia-Except-Once-Props`

### MergeProp
- contributes merge metadata
- supports append, prepend, and deep merge
- supports optional `matchOn`
- supports scroll metadata

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

## History and navigation flags

The response API must support:
- `WithEncryptHistory(...)`
- `WithClearHistory(...)`
- `WithPreserveFragment(...)`

Global history encryption defaults must be configurable through `InertiaOptions`.

## External redirects

External navigation must support:
- `409` + `X-Inertia-Location`
- `409` + `X-Inertia-Redirect` for external URLs containing fragments

## Infinite scroll

Infinite scroll support must include:
- merge metadata
- `scrollProps`
- merge-intent override via `X-Inertia-Infinite-Scroll-Merge-Intent`

## Compatibility requirements

The preferred public API is `Render(...)`.

Legacy `Inertia(...)` APIs may remain for compatibility, but they should be treated as deprecated rather than primary.
