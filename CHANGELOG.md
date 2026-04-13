# Changelog

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
