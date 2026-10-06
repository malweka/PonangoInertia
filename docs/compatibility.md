# Inertia.js Compatibility

This page lists what Ponango.Inertia 3.0 supports from the current Inertia.js release, feature by feature and
field by field.

| | Version checked |
|---|---|
| Inertia.js client (`@inertiajs/vue3`, `@inertiajs/react`, `@inertiajs/svelte`) | **3.8.0** (2026-10-01) |
| Reference server adapter (`inertiajs/inertia-laravel`) | **3.5.1** |
| Protocol specification | [inertiajs.com/docs/v3/core-concepts/the-protocol](https://inertiajs.com/docs/v3/core-concepts/the-protocol), checked 2026-10-06 |

Legend:

| Mark | Meaning |
|---|---|
| ✅ | Supported |
| ⚠️ | Partly supported, or needs app configuration (see the note) |
| ❌ | Not supported yet |
| ➖ | Client-side feature: needs nothing from the server beyond the protocol |

## Protocol

### Page object fields

| Field | Status | Notes |
|---|---|---|
| `component` | ✅ | |
| `props` | ✅ | Always includes `errors` (`{}` when empty) |
| `url` | ✅ | Request path by default; settable via `InertiaResult.Url` |
| `version` | ✅ | From `IAssetVersionProvider` |
| `encryptHistory` | ✅ | `InertiaOptions.EncryptHistory`, `WithEncryptHistory()` |
| `clearHistory` | ✅ | `WithClearHistory()` |
| `preserveFragment` | ✅ | `WithPreserveFragment()` |
| `mergeProps` / `prependProps` / `deepMergeProps` | ✅ | Root or nested paths (`posts.data`) |
| `matchPropsOn` | ✅ | |
| `scrollProps` | ✅ | Includes `reset`; page numbers or cursors |
| `deferredProps` | ✅ | Grouped |
| `rescuedProps` | ✅ | `Inertia.Defer(..., rescue: true)` |
| `sharedProps` | ✅ | Can be turned off with `ExposeSharedPropKeys = false` |
| `onceProps` | ✅ | `expiresAt` in Unix milliseconds, custom keys |
| `flash` | ✅ | Top-level field, backed by TempData |
| `preserveBigIntegers` | ✅ | With `{"$bigint": "…"}` markers |

### Request headers

| Header | Status | Notes |
|---|---|---|
| `X-Inertia` | ✅ | `InertiaContext.IsInertia` |
| `X-Inertia-Version` | ✅ | Compared on Inertia GET requests |
| `X-Inertia-Partial-Component` | ✅ | |
| `X-Inertia-Partial-Data` | ✅ | Dot-notation paths supported |
| `X-Inertia-Partial-Except` | ✅ | Dot-notation paths supported; applied after `Partial-Data` |
| `X-Inertia-Reset` | ✅ | Drops merge labels, sets scroll `reset` |
| `X-Inertia-Error-Bag` | ✅ | |
| `X-Inertia-Except-Once-Props` | ✅ | Ignored on partial reloads, as specified |
| `X-Inertia-Infinite-Scroll-Merge-Intent` | ✅ | Scroll props only |
| `Purpose: prefetch` | ✅ | `InertiaContext.IsPrefetch` |
| `Precognition` / `Precognition-Validate-Only` | ✅ | `[Precognitive]` |
| `X-XSRF-TOKEN` | ⚠️ | Not wired automatically; see [CSRF](#csrf-protection) |
| `X-Requested-With`, `Accept`, `Content-Type`, `Cache-Control` | ✅ | Handled by ASP.NET Core; nothing adapter-specific needed |

### Response headers and status codes

| Item | Status | Notes |
|---|---|---|
| `X-Inertia: true` on JSON page responses | ✅ | |
| `Vary: X-Inertia` | ✅ | Appended to existing `Vary` values |
| `409` + `X-Inertia-Location` (asset version mismatch) | ✅ | Also sends `X-Inertia-Version` |
| `409` + `X-Inertia-Location` (external redirect) | ✅ | `Location(url)`, or intercepted external redirects |
| `409` + `X-Inertia-Redirect` (redirect to a `#fragment` URL) | ✅ | Not applied to prefetch requests |
| `302` → `303` after non-GET requests | ✅ | Applied to every non-GET method (the spec requires it for PUT/PATCH/DELETE) |
| `204` + `Precognition-Success: true` | ✅ | |
| `422` with errors (Precognition) | ✅ | One message per field |
| `Vary: Precognition` | ✅ | On every response from a `[Precognitive]` action |

### Initial page

| Item | Status | Notes |
|---|---|---|
| `<script type="application/json" data-page="{appId}">` payload | ✅ | `Html.InertiaRender(...)` |
| `/` escaped as `\/` in the payload | ✅ | `<` is escaped as `<` too |
| Several Inertia apps on one page | ✅ | Distinct `appId` per app |

## Props and data

| Inertia feature | Status | Adapter API |
|---|---|---|
| Regular props | ✅ | `Render(component, props)`, `With(key, value)` |
| Lazy evaluation (closures) | ✅ | `(Func<object>)(() => …)` values |
| [Partial reloads](https://inertiajs.com/docs/v3/data-props/partial-reloads) | ✅ | `only` / `except`, including nested dot paths |
| Optional props | ✅ | `Inertia.Optional(...)` |
| Always props | ✅ | `Inertia.Always(...)` |
| [Deferred props](https://inertiajs.com/docs/v3/data-props/deferred-props) and groups | ✅ | `Inertia.Defer(..., group)` |
| Deferred props with rescue | ✅ | `rescue: true` / `.Rescue()` |
| [Merging props](https://inertiajs.com/docs/v3/data-props/merging-props): append, prepend, deep merge | ✅ | `Inertia.Merge(...)`, `Inertia.DeepMerge(...)` |
| Merging at nested paths, match on fields | ✅ | `.Append("data", matchOn: "id")`, `.Prepend(...)`, `.MatchingOn(...)` |
| Resetting props | ✅ | |
| [Once props](https://inertiajs.com/docs/v3/data-props/once-props) | ✅ | `Inertia.Once(...)` |
| Once: expiration, custom keys, forced refresh | ✅ | `.Until(...)`, `.As(key)`, `.Fresh()` |
| Shared once props | ✅ | `InertiaContext.ShareOnce(...)` |
| Combining types (deferred + merge, once on deferred/merge/optional) | ✅ | Fluent modifiers |
| Nested prop types and dot notation | ✅ | Walks anonymous objects and string-keyed dictionaries; classes, records and lists are sent whole |
| [Infinite scroll](https://inertiajs.com/docs/v3/data-props/infinite-scroll) | ✅ | `Inertia.Scroll(...)` with `ScrollMetadata.ForPage` / `ForCursor`, `.Defer()` |
| [Shared data](https://inertiajs.com/docs/v3/data-props/shared-data) | ✅ | `InertiaOptions.SharedData`, `InertiaContext.Share(...)` |
| [Flash data](https://inertiajs.com/docs/v3/data-props/flash-data) | ✅ | `WithFlash(...)`, `InertiaContext.Flash(...)`; survives redirects |
| [Big integers](https://inertiajs.com/docs/v3/advanced/big-integers) | ✅ | `PreserveBigIntegers`, `WithPreserveBigIntegers()`; requires client 3.8.0+ |
| `ProvidesInertiaProperty` / `ProvidesInertiaProperties` | ❌ | Laravel-specific interfaces; not ported |

## Server features

| Inertia feature | Status | Notes |
|---|---|---|
| [Responses](https://inertiajs.com/docs/v3/the-basics/responses) | ✅ | `InertiaContext.Render(...)`, `InertiaController.Render(...)` |
| Root template data | ⚠️ | The page model is the Razor view's model; extra values go through `ViewData`. There is no dedicated `withViewData` API |
| [Redirects](https://inertiajs.com/docs/v3/the-basics/redirects) | ✅ | Standard MVC redirects; `303` conversion by the middleware |
| External redirects | ✅ | `Location(url)` |
| Preserving fragments | ✅ | `WithPreserveFragment()`, and `#fragment` redirect handling |
| Store previous URL | ❌ | Laravel session feature; use the `Referer` header |
| [Asset versioning](https://inertiajs.com/docs/v3/advanced/asset-versioning) | ✅ | `IAssetVersionProvider`, checked by `UseInertia()` |
| [Validation](https://inertiajs.com/docs/v3/the-basics/validation) and error bags | ⚠️ | `WithErrors(ModelState, bag)` and the `X-Inertia-Error-Bag` header. Errors are returned when the page is re-rendered; they are not persisted across a redirect the way Laravel's session does |
| Multiple errors per field | ✅ | `InertiaOptions.WithAllErrors` |
| [Precognition](https://inertiajs.com/docs/v3/the-basics/forms#precognition) | ✅ | `[Precognitive]` with `Precognition-Validate-Only` |
| [History encryption](https://inertiajs.com/docs/v3/security/history-encryption) | ✅ | Global option and per response; clear history |
| [CSRF protection](https://inertiajs.com/docs/v3/security/csrf-protection) | ⚠️ | Use ASP.NET Core antiforgery: issue an `XSRF-TOKEN` cookie and accept the `X-XSRF-TOKEN` header (`AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN")`). The adapter does not set this up |
| [Prefetching](https://inertiajs.com/docs/v3/data-props/prefetching) | ✅ | Normal pipeline; `IsPrefetch` to skip side effects |
| [Instant visits](https://inertiajs.com/docs/v3/the-basics/instant-visits) | ✅ | Relies on `sharedProps`, which is emitted by default |
| [Error handling](https://inertiajs.com/docs/v3/advanced/error-handling) (custom error pages) | ⚠️ | No helper like `handleExceptionsUsing`; render an Inertia page from your own exception or status-code handler |
| Empty response → redirect back | ❌ | Return an explicit redirect |
| [Server-side rendering](https://inertiajs.com/docs/v3/advanced/server-side-rendering) | ❌ | Pages render client-side only |
| [DevTools](https://inertiajs.com/docs/v3/advanced/devtools) server recorder | ❌ | The browser extension still records client-side data |
| [Testing](https://inertiajs.com/docs/v3/advanced/testing) helpers (`assertInertia`) | ❌ | Assert on the JSON page object with your own helpers |

## Client-side features

These features live in the client adapter and work with any server that follows the protocol. They need no
adapter support beyond what's listed above.

| Inertia feature | Status | Server requirement |
|---|---|---|
| [Pages](https://inertiajs.com/docs/v3/the-basics/pages), [layouts](https://inertiajs.com/docs/v3/the-basics/layouts), layout props | ➖ | |
| [Title & meta](https://inertiajs.com/docs/v3/the-basics/title-and-meta) (`<Head>`) | ➖ | Head elements in your Razor layout use the `data-inertia` attribute |
| [Links](https://inertiajs.com/docs/v3/the-basics/links) and [manual visits](https://inertiajs.com/docs/v3/the-basics/manual-visits) | ➖ | |
| [Forms](https://inertiajs.com/docs/v3/the-basics/forms) (`useForm`, `<Form>`) | ➖ | Validation errors in `props.errors` |
| [File uploads](https://inertiajs.com/docs/v3/the-basics/file-uploads) | ➖ | Standard multipart model binding |
| [HTTP requests](https://inertiajs.com/docs/v3/the-basics/http-requests) (`useHttp`) | ➖ | Plain JSON endpoints |
| [Optimistic updates](https://inertiajs.com/docs/v3/the-basics/optimistic-updates) | ➖ | |
| [View transitions](https://inertiajs.com/docs/v3/the-basics/view-transitions) | ➖ | |
| [Polling](https://inertiajs.com/docs/v3/data-props/polling) | ➖ | Uses partial reloads |
| [Load when visible](https://inertiajs.com/docs/v3/data-props/load-when-visible) (`<WhenVisible>`) | ➖ | Uses partial reloads (dot paths supported) |
| [Remembering state](https://inertiajs.com/docs/v3/data-props/remembering-state) | ➖ | |
| [Scroll management](https://inertiajs.com/docs/v3/advanced/scroll-management), [progress indicators](https://inertiajs.com/docs/v3/advanced/progress-indicators), [events](https://inertiajs.com/docs/v3/advanced/events) | ➖ | |
| [Code splitting](https://inertiajs.com/docs/v3/advanced/code-splitting), [TypeScript](https://inertiajs.com/docs/v3/advanced/typescript) | ➖ | |

## Differences from the reference adapter

Where Ponango.Inertia intentionally differs from `inertia-laravel`:

| Area | Laravel | Ponango.Inertia |
|---|---|---|
| External redirects | Only through `Inertia::location()` | Also detected automatically by the middleware (other host or port) and always answered with `X-Inertia-Location` |
| `302` → `303` | PUT, PATCH, DELETE | Every non-GET method (equivalent for browsers) |
| Nested props | Walks every array | Walks anonymous objects and string-keyed dictionaries; classes, records and lists are sent whole |
| Merge modifiers | `append(path)` needs a prior `merge()` on deferred props | `Append(path)` / `Prepend(path)` turn merging on |
| Once modifiers | `as()` / `until()` need `once()` | `As(...)` / `Until(...)` imply `Once()` |
| Configuration | `config/inertia.php`, middleware class | `AddInertia(options => …)`, `UseInertia()` |
