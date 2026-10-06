# Changelog

## 3.0.0 (unreleased)

Aligns the adapter with the current Inertia.js v3 protocol. See [docs/upgrading-to-3.0.md](docs/upgrading-to-3.0.md).

### Added

- Version-mismatch `409` responses now echo the current asset version in `X-Inertia-Version`.
- Precognitive actions add `Precognition` to the `Vary` header on every response.
- `InertiaResult.WithFlash(IDictionary<string, object?>)` flashes several values at once.

### Fixed

- Once-prop `expiresAt` is now a Unix timestamp in milliseconds, as the client expects. It was emitted in
  seconds, so every once prop with an expiry looked expired and was fetched again on every visit.
- Partial reloads that send both `X-Inertia-Partial-Data` and `X-Inertia-Partial-Except` now remove the
  excepted props. The except list used to be ignored whenever a data list was present.
- The initial-page script payload escapes every `/` as `\/` and every `<` as `\u003c`, so prop data can't close
  the script element early, whatever the configured JSON encoder or casing of `</script>`.

### Breaking changes

- Flash data is now emitted in the page object's top-level `flash` field, as Inertia v3 expects, instead of
  `props.flash`. Read it with `usePage().flash` or the `flash` event; the client no longer stores it in history,
  so it does not reappear on Back. A prop you share as `flash` is now an ordinary prop and is not merged with
  flashed values, and `flash` no longer appears in `sharedProps`.
- `props.errors` is now always present, as `{}` when there are no errors.
- Partial reloads that send only `X-Inertia-Partial-Except` now also resolve optional and deferred props that
  are not excluded, matching the reference adapter. Full visits still never resolve them.
- Redirects intercepted by the middleware during Inertia requests:
  - an external redirect whose target contains a `#fragment` now returns `409` + `X-Inertia-Location` (it used
    `X-Inertia-Redirect`, which made the client fetch another origin by XHR);
  - an internal redirect whose target contains a `#fragment` now returns `409` + `X-Inertia-Redirect`, so the
    client keeps the fragment (prefetch requests excepted).

## 2.1.0

First release published to nuget.org.

### Added

- Published to nuget.org as `Ponango.Inertia`. Earlier versions were distributed as a binary only.
- Symbol package (`.snupkg`) with SourceLink, so consumers can step into library sources while debugging.
- MIT license.

### Changed

- The initial-payload `<script>` tag now carries `data-page="{appId}"` instead of a valueless `data-page`
  attribute, so several Inertia apps on one page can each be matched to their own payload. Selectors of the
  form `[data-page]` are unaffected.

## 2.0.0

### Added

- Inertia v3-style initial HTML payload using a JSON script tag
- page object support for:
  - `encryptHistory`
  - `clearHistory`
  - `preserveFragment`
  - `deferredProps`
  - `mergeProps`
  - `prependProps`
  - `deepMergeProps`
  - `matchPropsOn`
  - `scrollProps`
  - `sharedProps`
  - `onceProps`
- prop wrapper types:
  - `OptionalProp`
  - `AlwaysProp`
  - `DeferredProp`
  - `MergeProp`
  - `OnceProp`
- async callback support for wrapper props
- middleware support via `app.UseInertia()`
- shared data configuration via `InertiaOptions.SharedData`
- error bag support
- precognition support with `[Precognitive]`
- flash messaging support backed by TempData
- history encryption and clear-history response flags
- prefetch detection via `InertiaContext.IsPrefetch`
- infinite scroll helpers via `ScrollPropConfig` and `MergeProp.WithScroll(...)`
- ergonomic API improvements:
  - `Render(...)`
  - `Location(...)`
  - `With(...)`
  - `WithFlash(...)`
  - static `Inertia` factory helpers
- automated tests for protocol and API behavior

### Changed

- `Render(...)` is now the preferred rendering API
- `RootView` can be configured via `InertiaOptions`
- `sharedProps` metadata now reflects the final emitted payload
- middleware appends `X-Inertia` to `Vary` instead of replacing the header

### Deprecated

- `LazyProp` in favor of `OptionalProp`
- legacy `Inertia(...)` rendering methods in favor of `Render(...)`

### Breaking changes

- applications should add `app.UseInertia()` to the middleware pipeline
- the initial HTML response uses a script-tag page payload instead of the old `data-page` attribute format
