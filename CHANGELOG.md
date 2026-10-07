# Changelog

## 3.0.0 (unreleased)

Aligns the adapter with the current Inertia.js v3 protocol. See [docs/upgrading-to-3.0.md](docs/upgrading-to-3.0.md).

Pre-releases on nuget.org (`dotnet add package Ponango.Inertia --prerelease`):

- `3.0.0-beta.1` (2026-10-06): everything below.

### Added

- `docs/compatibility.md`: feature-by-feature compatibility tables against Inertia.js 3.8.0, and a README
  overview of Inertia and of what the adapter provides.
- Version-mismatch `409` responses now echo the current asset version in `X-Inertia-Version`.
- Precognitive actions add `Precognition` to the `Vary` header on every response.
- `InertiaResult.WithFlash(IDictionary<string, object?>)` flashes several values at once.
- Composable prop modifiers, following the reference adapter:
  - deferred props can merge (`Merge`, `DeepMerge`, `Append`, `Prepend`, `MatchingOn`) and be remembered (`Once`);
  - merge and optional props can be remembered (`Once`);
  - once options `As(key)` (custom key shared across pages), `Fresh()`, and `Until(TimeSpan | DateTimeOffset)`.
- Rescued deferred props: `Inertia.Defer(..., rescue: true)` or `.Rescue()` omits a failing prop, logs the
  exception, and lists it in the new `rescuedProps` page field.
- Merging at nested paths: `Inertia.Merge(...).Append("data", matchOn: "id")` emits `mergeProps: ["users.data"]`,
  plus `Prepend(path)` and dictionary overloads for several paths.
- `Inertia.DeepMerge(...)` factory and `InertiaContext.ShareOnce(...)`.
- Big integer support: `InertiaOptions.PreserveBigIntegers` and `InertiaResult.WithPreserveBigIntegers(...)` send
  integers outside JavaScript's safe range (in props and flash) as `{"$bigint": "..."}` markers and set the
  `preserveBigIntegers` page flag, so Inertia 3.8+ clients receive exact `BigInt` values.
- Nested prop types and dot notation: wrappers and lazy delegates inside anonymous objects and string-keyed
  dictionaries are resolved, their metadata uses dot paths (`auth.notifications`), and partial reloads can target
  nested paths (`router.reload({ only: ["auth.notifications"] })`). Containers without wrappers serialize as before.
- `InertiaOptions.WithAllErrors` sends every validation message per field as an array.
- Validation errors are kept across a redirect, as Laravel does with its session: an Inertia non-GET request that
  redirects with an invalid `ModelState` delivers the errors in `props.errors` of the next rendered page (captured
  by an MVC filter that `AddInertia` registers). `InertiaContext.FlashErrors(...)` stores errors explicitly, for
  minimal APIs or custom validation, and wins over the automatic capture in the same request.
  `InertiaOptions.PersistValidationErrorsOnRedirect` (default `true`) turns the automatic capture off.
- `Back(fallbackUrl)` on `InertiaContext` and `InertiaController` redirects to a same-host `Referer` whose path is
  local, else to the fallback URL, which must be a local path.
- `InertiaOptions.ExposeSharedPropKeys` (default `true`) can turn off the `sharedProps` list.
- Lazy delegate props: a prop value that is a delegate taking no arguments and returning a value (`Func<object>`,
  `Func<int>`, `Func<Task<T>>`, `Func<ValueTask<T>>`) is only evaluated when the response includes it. It used to
  be handed to the serializer as is.
- `Inertia.Scroll(...)` / `ScrollProp` for infinite scroll, matching `Inertia::scroll()`: merges the array under a
  wrapper key (`mergeProps: ["posts.data"]`), supports page numbers and cursors (`ScrollMetadata.ForPage`,
  `ScrollMetadata.ForCursor`), `MatchingOn`, and `.Defer()`.

### Changed

- `X-Inertia-Infinite-Scroll-Merge-Intent` now only affects scroll props (`MergeProp.WithScroll(...)`), as in the
  reference adapter. Plain merge props keep their configured append/prepend mode.
- `MergeProp.WithScroll(...)` is obsolete in favor of `Inertia.Scroll(...)`. Its `scrollProps` entry now includes
  `reset` (`true` when the prop is reset) and emits `null` page values explicitly instead of omitting them.
- Shared props are emitted before page props in `props` (page props still win on key conflicts).
- Validation errors from an Inertia form request that redirects with an invalid `ModelState` now reach the next
  page instead of being dropped. Set `InertiaOptions.PersistValidationErrorsOnRedirect = false` to opt out.
- The `InertiaContext` constructors gained an optional `InertiaValidationErrors? validationErrors` parameter.
- Serializer options are built once per `IJsonSerializerOptionBuilder` instance and reused, instead of on every
  response. The `InertiaOptions.JsonSerializerOptions` callback therefore runs once and should not depend on
  the current request.

### Fixed

- A once prop skipped because of `X-Inertia-Except-Once-Props` keeps its `onceProps` entry, so the client keeps
  remembering it. It used to disappear, so the client forgot it and fetched it again on the next visit.
- Partial reloads always resolve a requested once prop, ignoring `X-Inertia-Except-Once-Props`, so
  `router.reload({ only: [...] })` can refresh it.
- Once-prop `expiresAt` is now a Unix timestamp in milliseconds, as the client expects. It was emitted in
  seconds, so every once prop with an expiry looked expired and was fetched again on every visit.
- Partial reloads that send both `X-Inertia-Partial-Data` and `X-Inertia-Partial-Except` now remove the
  excepted props. The except list used to be ignored whenever a data list was present.
- `page.url` now includes the path base and the query string (`/users?page=2`). It was the request path only,
  so the client dropped the query string from the address bar and from history.
- `sharedProps` now lists every shared key that a page prop does not override, including on partial reloads
  that skip the shared values, as in the reference adapter. It used to list only the keys present in the
  response, so a partial reload (a deferred prop loading, for example) cleared the client's list and later
  instant visits lost the shared props.
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
- Top-level prop keys containing dots (`["auth.user"] = ...`) are now unpacked into nested objects (`auth.user`),
  as in the reference adapter. They used to be emitted as literal `"auth.user"` keys.
- `Inertia.Defer(...)` and the `DeferredProp` constructors gained an optional `rescue` parameter, and
  `DeferredProp`, `MergeProp` and `OptionalProp` now inherit their fluent modifiers from the new
  `MergeModifiers<TSelf>` / `OnceModifiers<TSelf>` base classes. Source compatible, but code compiled against
  2.x must be recompiled.

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
