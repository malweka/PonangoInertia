# Plan: Catch up with Inertia.js 3.9–3.11 and inertia-laravel 3.5.2–3.7.0

- **Status:** all five phases done on branch `feat/inertia-3.11-catch-up` (one commit per phase, see
  [Execution notes](#9-execution-notes)). The npm organization `ponango` exists (D6); the first npm publish is
  manual (see `scripts/release.sh`).
- **Written:** 2026-11-05, against `main` at `89d6890`. Baseline: 177 tests pass, 0 fail.
- **Source notes:** `ai/tmp/inertia-3.11-catch-up.md` (local, not committed). This plan replaces them.
- **Target release:** 3.0.0 is still unreleased (`3.0.0-beta.1` is on nuget.org), so this work goes into the
  `## 3.0.0 (unreleased)` section of `CHANGELOG.md` unless decision D1 says otherwise.
- **Executor:** an AI coding agent. Read `AGENTS.md` and `README.md` first, and follow the rules in
  `ai/plans/inertia-v3-protocol-alignment.md` §0.1: test first, CRLF line endings, docs in the same change, keep
  the compatibility APIs. Use the `-p:` build switches from `AGENTS.md`.

---

## 0. What changed upstream

| Release | Date | What matters for us |
|---|---|---|
| client 3.9.0 / 3.9.1 | 2026-10-09 | Client-only fixes. Since 3.9.1 the `X-XSRF-TOKEN` header is only sent to same-origin URLs |
| client 3.10.0 | 2026-10-30 | `QUERY` method; shared once props on instant visits (#3356) |
| client 3.11.0 | 2026-11-02 | Live props (#3232), `X-Socket-Id` header, `useProp()` |
| inertia-laravel 3.5.2 | 2026-10-21 | Stricter "empty response" test; `Vary` appended (we already do this) |
| inertia-laravel 3.6.0 | 2026-10-30 | `QUERY` in the 302 → 303 list (#927) |
| inertia-laravel 3.7.0 | 2026-11-02 | Live props: `Inertia::live()`, `->live()`, `liveProps`, `Inertia::broadcastProps()` (#901) |

Nothing to do for: polling `background` option, `type` on the `navigate` event, unique IV per encrypted history
entry, prefetch cache flushed on clear history, relative `Link`/`Form` URLs, stale-response fixes, SSR fixes.

Reference source read for this plan (inertia-laravel `3.x`): `src/HasLiveUpdates.php`, `src/LiveProp.php`,
`src/SupportsLiveUpdates.php`, `src/MergesProps.php`, `src/PropsResolver.php`, `src/ResponseFactory.php`,
`src/Middleware.php`; client `packages/core/src/live.ts` and `socketId.ts` (inertiajs/inertia `3.x`). Re-read
them before Phase 3; they are short.

## 1. Decisions for the maintainer

Each has a recommendation. The executor uses the recommendation unless the maintainer says otherwise.

| # | Question | Recommendation |
|---|---|---|
| D1 | Release vehicle | Ship in 3.0.0 (next pre-release `3.0.0-beta.2`), since 3.0.0 is not out yet. Phases 1–2 can go out first if Phase 3 takes longer |
| D2 | Empty response → redirect back: on by default? | Yes, like Laravel, with `InertiaOptions.RedirectEmptyResponsesBack` (default `true`) to turn it off. It is a behavior change for apps that return `Ok()` with no body to an Inertia visit, so it gets a CHANGELOG "Changed" line |
| D3 | How live props name events and channels (no `broadcastOn` / `broadcastAs` in .NET) | Strings plus a `LiveChannel` type, and an optional `ILiveEvent` interface (`EventName`, `Channels`) so an event object can be passed the way Laravel passes `new OrderUpdated($order)` |
| D4 | SignalR support | **Decided (2026-11-05):** ship it as two packages in this repo: the NuGet package `Ponango.Inertia.SignalR` (Phase 4) and the npm package `@ponango/inertia-signalr` (Phase 5). Docs alone were rejected: nobody will write the transport themselves |
| D5 | `BroadcastProps` envelope | Match Laravel (`{ "__inertia": { "props": … } }`), plus optional `event` and `socketId` arguments that fill `__inertia.event` / `__inertia.socketId`, which the client already reads (`live.ts`: channel transports and "a broadcaster that cannot exclude a connection"). SignalR needs both for a channel transport |
| D6 | npm scope | **Pending:** the maintainer creates the free npm organization `ponango` (public packages only). Until then Phase 5 can be built and tested, but not published |
| D7 | Versioning of the three packages | One version for all three (lockstep): tag `v3.0.0-beta.2` publishes `Ponango.Inertia`, `Ponango.Inertia.SignalR` and `@ponango/inertia-signalr` at `3.0.0-beta.2`. `Ponango.Inertia.SignalR` depends on the exact same `Ponango.Inertia` version |

## 2. Phase overview

| Phase | Theme | Size | Wire change? |
|---|---|---|---|
| 1 | `QUERY` method, shared once regression test, compatibility version bump | S | No (tests and docs only, unless a test finds a gap) |
| 2 | Empty response → redirect back | S | Yes (new behavior, D2) |
| 3 | Live props in `Ponango.Inertia`: `liveProps`, `Live(...)`, `X-Socket-Id`, `BroadcastPropsAsync` | L | Yes (new page field) |
| 4 | `Ponango.Inertia.SignalR` (NuGet): hub, channel authorization, broadcaster | M | No (new package) |
| 5 | `@ponango/inertia-signalr` (npm): the client transport, release tooling, end-to-end sample | M | No (new package) |

One branch and one PR per phase, from `main`: `feat/query-method`, `feat/empty-response-redirect`,
`feat/live-props`, `feat/signalr-server`, `feat/signalr-client`. One commit per phase (Phases 3–5 may use one
commit per step). Phases 4 and 5 depend on Phase 3; Phase 5's end-to-end test depends on Phase 4.

---

## 3. Phase 1: `QUERY`, shared once props, compatibility bump

### 3.1 Background

`QUERY` is a safe, idempotent method like GET, with the data in the request body. The client adds
`router.query()`, `<Link method="query">`, `<Form method="query">`, `form.query()`, `http.query()`. A page rendered by
a `QUERY` visit remembers the body, so partial reloads, deferred props, polling and infinite scroll resend it as
`QUERY`. It can be prefetched (cache keyed by body).

Laravel's only server change: `QUERY` joined the 302 → 303 list (`Middleware.php:178`).

Current code, verified on 2026-11-05:

| Location | Behavior | `QUERY` result |
|---|---|---|
| `InertiaMiddleware.cs:84-88` | 302 → 303 for every non-GET Inertia request | Converted ✅ |
| `InertiaRedirectResult.cs:18` | Non-GET gets the 303 path | ✅ |
| `InertiaValidationErrorsFilter.cs:33` | Captures errors for non-GET redirects | Kept across a redirect ✅ (Laravel keeps session errors for any method) |
| `InertiaMiddleware.cs:39`, `InertiaResult.cs:304` | Asset version check is GET-only | Not checked. Laravel is GET-only too (`Middleware.php:170`). Keep |

So this phase should need no production code. Each test below must **pass** without code changes; if one fails,
fix the code and add a CHANGELOG "Fixed" line.

### 3.2 Tests (`tests/Ponango.Inertia.Tests/`)

Add a `QueryMethodTests.cs` (or a region in `ProtocolComplianceTests`) using the existing test server helpers in
`TestInfrastructure.cs`:

1. `QUERY` Inertia request whose action redirects with 302 → response is 303 with the same `Location`.
2. `QUERY` request with an invalid `ModelState` that redirects → the follow-up GET renders the errors in
   `props.errors` (mirror an existing case in `ValidationErrorRedirectTests`).
3. `QUERY` request rendering a page → JSON page object, `X-Inertia: true`, `Vary: X-Inertia`.
4. `QUERY` request with a stale `X-Inertia-Version` → **no** 409 (GET-only, as in Laravel).
5. `QUERY` request with `Purpose: prefetch` redirecting to a `#fragment` URL → plain redirect, not
   `409 + X-Inertia-Redirect` (fragment redirect skipped for prefetch). Without `Purpose: prefetch` → 409.
6. `[Precognitive]` action reached with `QUERY` + `Precognition: true` → 204 + `Precognition-Success: true` when
   valid, 422 with errors when invalid (mirror `PrecognitionTests`).
7. Partial reload via `QUERY` (`X-Inertia-Partial-Component` + `X-Inertia-Partial-Data`) → only the selected prop.

Routing in the tests: `[AcceptVerbs("QUERY")]` on a controller action, or `MapMethods(path, new[] { "QUERY" }, …)`.

Shared once props on instant visits (client #3356): the client fix relies on a `ShareOnce` key appearing in
**both** `sharedProps` and `onceProps`. Add to `PropsResolverTests` (next to
`Shared_once_prop_via_ShareOnce_is_cached_like_page_once_props`):

8. Full Inertia visit with `ShareOnce("countries", …)` → `sharedProps` contains `countries` and `onceProps` has a
   `countries` entry.
9. Same visit with `X-Inertia-Except-Once-Props: countries` → the prop value is skipped, but `sharedProps` still
   lists `countries` and `onceProps.countries` is still emitted. (If `sharedProps` drops it, compare with Laravel's
   `resolveSharedProps`, which records the shared keys before any exclusion, and fix.)

### 3.3 Docs

- `docs/advanced-topics.md`: a short "`QUERY` requests" section:
  - routing: `[AcceptVerbs("QUERY")]`, a small `HttpQueryAttribute : HttpMethodAttribute`, or `app.MapMethods("/products", new[] { "QUERY" }, …)`;
  - binding: `[FromBody]` for JSON (the client default), `[FromForm]` when files are sent;
  - antiforgery: `AutoValidateAntiforgeryToken` only skips GET, HEAD, OPTIONS and TRACE, so `QUERY` **is**
    validated, like any POST. The `X-XSRF-TOKEN` setup from the CSRF section is required (Laravel's
    `VerifyCsrfToken` also checks `QUERY`). Since client 3.9.1 the header is only sent to same-origin URLs;
  - redirects after `QUERY` become 303, validation errors survive the redirect, asset versions are not checked.
- Verify Kestrel accepts `QUERY` (write the integration test against `TestServer`, then one manual `curl -X QUERY`
  against a Kestrel sample if one exists). Note IIS request filtering (`<verbs allowUnlisted>`) in the docs as
  something to check; do not claim it works without trying.
- `docs/compatibility.md`:
  - version table → client **3.11.0** (2026-11-02), inertia-laravel **3.7.0**, protocol page re-checked on the
    execution date (diff it against the 2026-10-06 reading: look for `liveProps`, `X-Socket-Id`, `QUERY`);
  - `302 → 303` row: note `QUERY` explicitly; "Differences" table: Laravel now lists `PUT, PATCH, DELETE, QUERY`;
  - new client-side row: `QUERY` visits (`router.query`, `<Form method="query">`) ➖ with the server note above;
  - add placeholder ❌ rows for `liveProps` and `X-Socket-Id` (Phase 3 turns them ✅), and a client-side row for
    `useProp()`.
- `requirements/protocol-requirements.md`: `QUERY` is non-GET for 303 conversion and error capture, and GET-only
  rules (asset version) do not apply to it.
- `CHANGELOG.md` (3.0.0 unreleased): "Added: `QUERY` requests are covered by tests and documented; compatibility
  page checked against Inertia.js 3.11.0 and inertia-laravel 3.7.0."

### 3.4 Done when

All new tests pass, the full suite is green, docs updated, one commit.

---

## 4. Phase 2: Empty response → redirect back

### 4.1 Reference behavior (`Middleware.php`, 3.5.2+)

```php
if ($response->isOk() && $response->getContent() === '') {
    $response = $this->onEmptyResponse($request, $response);   // Redirect::back()
}
```

- Applies to **every** Inertia request (any method, GET included), not only non-GET.
- Runs before the 302 → 303 conversion, so a non-GET request ends up with 303.
- 3.5.2 tightened "empty": streamed responses and a body of `"0"` no longer count.

### 4.2 Design

In `InertiaMiddleware.InvokeAsync`, after `await _next(context)` and **before** the external/fragment redirect
block and the 303 conversion:

```csharp
if (inertiaContext.IsInertia
    && options.RedirectEmptyResponsesBack
    && !context.Response.HasStarted
    && context.Response.StatusCode == StatusCodes.Status200OK
    && IsEmptyBody(context.Response))           // no ContentLength, or ContentLength == 0, and nothing written
{
    // Same target rules as Back(): same-host Referer with a local path, else PathBase + "/".
    context.Response.Redirect(InertiaExtensions.ResolveBackUrl(context, fallback: "/"));   // 302, then 303 below for non-GET
}
```

- An action that writes any body starts the response (`HasStarted == true`), so streamed responses and `"0"` are
  excluded the same way Laravel now excludes them. Confirm this with a test rather than assuming it: under
  `TestServer`, check whether `HasStarted` flips on the first write; if it does not, track writes with a small
  `IHttpResponseBodyFeature` wrapper or compare `Response.Body` position instead.
- Reuse the `Back()` URL logic. If it lives inside `InertiaExtensions.Back(...)` today, extract an internal helper
  so both use the same same-host/local-path checks. Do not duplicate it.
- Add `InertiaOptions.RedirectEmptyResponsesBack` (default per D2) with an XML doc comment.
- A `204 No Content` is not `200`, so it is left alone (Laravel's `isOk()` is 200 only).

### 4.3 Tests

1. Inertia POST, action returns `Ok()` with no body, same-host `Referer: /users/create` → 303 to `/users/create`.
2. Inertia GET, empty 200 → 302 to the referer (Laravel does this for GET too).
3. No referer → redirect to `/` (with a path base, to the path base).
4. External referer → redirect to the fallback, not the external host.
5. Action writes `"0"` → 200 with body `"0"`, untouched.
6. Streamed response (writes and flushes) → untouched.
7. Non-Inertia request with empty 200 → untouched.
8. `204` → untouched.
9. Option off → untouched.
10. Flash data and validation errors set in the request survive the redirect (TempData), like with an explicit
    `Back()`.

### 4.4 Docs

- `docs/compatibility.md`: "Empty response → redirect back" ❌ → ✅ with the option name.
- `docs/advanced-topics.md` (redirects section): one paragraph.
- `README.md`: remove it from "Not Supported Yet" if listed.
- `requirements/protocol-requirements.md` and `requirements/public-api-requirements.md` (new option).
- `AGENTS.md`: add the behavior to the `InertiaMiddleware` responsibilities list.
- `CHANGELOG.md`: "Added" for the feature, "Changed" for the default (D2).

---

## 5. Phase 3: Live props

### 5.1 What the feature does

A live prop refreshes itself when a broadcast event arrives. The page object lists, per prop, which channels and
events to listen to. The client subscribes through a transport (Echo, Pusher, Action Cable, or a custom one) and,
when an event arrives, either takes the prop value from the event payload (`__inertia.props[path]`) or queues a
throttled partial reload (`router.reload({ only: [...] })`).

### 5.2 Wire format

```json
"liveProps": {
  "order": {
    "listeners": [
      { "channel": { "name": "orders.1", "type": "private" }, "events": ["order.updated"] }
    ],
    "throttle": 4000
  }
}
```

- Keyed by prop dot path. `throttle` omitted when not set. The whole field omitted when empty.
- Channel `type`: `public`, `private`, `presence`, `encrypted-private`. `name` without Laravel's prefixes.
- Listeners grouped by `type:name` in first-seen order; duplicate events in one channel dropped.
- Broadcast payload envelope: `{ "__inertia": { "props": { "<path>": value }, "event"?: "...", "socketId"?: "..." } }`.

### 5.3 Public API (proposed, per D3/D5)

```csharp
// Channel
public sealed record LiveChannel(string Name, LiveChannelType Type)
{
    public static LiveChannel Public(string name);
    public static LiveChannel Private(string name);
    public static LiveChannel Presence(string name);
    public static LiveChannel EncryptedPrivate(string name);
    public static implicit operator LiveChannel(string name);   // bare string = public, as in Laravel
}
public enum LiveChannelType { Public, Private, Presence, EncryptedPrivate }   // serialized as kebab-case strings

// Optional event contract, the .NET stand-in for broadcastAs()/broadcastOn()
public interface ILiveEvent
{
    string EventName { get; }
    IEnumerable<LiveChannel> Channels { get; }
}

// Factory
Inertia.Live(object? value, string on, LiveChannel channel, int? throttle = null)
Inertia.Live(object? value, IEnumerable<string> on, LiveChannel channel, int? throttle = null)
Inertia.Live(object? value, ILiveEvent on, int? throttle = null)
Inertia.Live(object? value, IEnumerable<ILiveEvent> on, int? throttle = null)

// Modifier, on LiveProp, OptionalProp, DeferredProp, OnceProp, AlwaysProp (same overloads, returns TSelf)
.Live(on, channel, throttle) / .Live(ILiveEvent on, throttle) …

// Broadcast payload
Task<IDictionary<string, object?>> Inertia.BroadcastPropsAsync(object props, string? @event = null, string? socketId = null)

// Socket id
InertiaRequestHeaders.SocketId        // X-Socket-Id
InertiaContext.SocketId
InertiaController.SocketId
```

Name the throttle `TimeSpan? throttle` instead of `int?` milliseconds if that reads better next to `Until(TimeSpan)`;
serialize it as milliseconds either way. Decide while writing the API and record it in the CHANGELOG.

Rules carried over from `HasLiveUpdates.php` (use `InvalidOperationException` for Laravel's `LogicException`):

- Calling `Live(...)` on a prop that already merges throws `"Live updates are not supported on merge properties."`.
  Calling a merge modifier (`Merge`, `DeepMerge`, `Append`, `Prepend`) on a live prop throws the same
  (`MergesProps.php:52`). `MergeProp` always merges, so `MergeProp.Live(...)` always throws.
- Several `Live(...)` calls add declarations; each declaration's channel applies only to its own events.
  `throttle` keeps the last non-null value.
- At least one event across all declarations, else throw when the listeners are built (at metadata collection,
  like Laravel, so a later `Live(...)` call can still add events).
- A string event needs an explicit channel (Laravel throws when it cannot derive channels). An `ILiveEvent` gives
  its own channels unless a channel is passed.
- Channel names passed with a Laravel-style prefix (`private-orders.1` with type `Private`) have the prefix
  stripped, so names copied from Pusher/Echo setups work.

### 5.4 Where it goes (follow `AGENTS.md`)

- `PropCapabilities.cs`: `internal interface ILiveProp { bool IsLive { get; } IReadOnlyList<LiveListener> LiveListeners(); int? LiveThrottle { get; } }`.
- `PropBehaviors.cs`: a `LiveBehavior` holding the declarations, the listener builder (grouping, dedupe, prefix
  stripping), and validation. Same pattern as `OnceBehavior` / `MergeBehavior`.
- `PropModifiers.cs`: `LiveModifiers<TSelf>` as the new **root** of the modifier chain:
  `OnceModifiers<TSelf> : LiveModifiers<TSelf>`, so `OptionalProp`, `DeferredProp` (and `MergeProp`, which throws)
  get `Live(...)`. `OnceProp` and `AlwaysProp` don't use the modifier bases today; make them derive from
  `LiveModifiers<OnceProp>` / `LiveModifiers<AlwaysProp>`. Changing a public class's base from `object` to a new
  abstract base only adds members, so it is source and binary compatible for callers; check that no public
  constructor or property changes. `MergeModifiers` merge methods check `IsLive` and throw.
- `LiveProp.cs`: `public class LiveProp : LiveModifiers<LiveProp>, IResolvableProp`; resolves its value like a
  lazy delegate (a no-argument delegate is invoked, otherwise the value is returned).
- `Inertia.cs`: `Live(...)` factories and `BroadcastPropsAsync(...)`.
- `PageModel.cs`: `LiveProps` (`Dictionary<string, LivePropMetadata>?`), serialized as `liveProps`, omitted when
  null like the other metadata. `InertiaResult.cs` copies it from the resolver (next to `OnceProps`, line ~262).
- `InertiaRequestHeaders.cs` / `InertiaContext.cs` (header parsing near line 159) / `InertiaController.cs`:
  `SocketId`.

### 5.5 Resolver (`PropsResolver.cs`), following `PropsResolver.php`

Read the PHP methods named here before editing; port the behavior, not the PHP structure.

1. `excludeFromInitialResponse` (PHP 406): **before** any exclusion, if the prop is live, collect its live
   metadata with `parentWasResolved: false`. A deferred or optional live prop left out of the full visit still gets
   its `liveProps` entry (the client must listen before the deferred reload lands).
2. `collectMetadata` (PHP 567): for included props, collect live metadata with the real `parentWasResolved`.
3. `collectLiveMetadata` (PHP 663): on a partial reload, skip unless the prop bypasses partial filtering (always
   props, or a child of a resolved parent: `bypassesPartialFiltering`) or `isIncludedInPartialMetadata(path)`
   (passes `only`, not in `except`). Our resolver already has equivalents for the partial checks; reuse them.
4. `isPropType` (PHP 554): live props count as prop types for the nested walk in `PropContainers`.
5. Optional refactor, same behavior: a `PathIsSameOrDescendant(path, parent)` helper for `only`/`except` matching
   (PHP 744), if our code repeats the `path == p || path.StartsWith(p + ".")` test.
6. Expose `LiveProps` on the resolver; emitted on every response, partial reloads included.

`BroadcastPropsAsync` ports `resolveBroadcastProps` (PHP 197): build a resolver with empty request headers (it
runs outside a request, e.g. in a background service), resolve each value like a page prop, walk nested
anonymous objects and string-keyed dictionaries, **drop** rescued props (not null), keep dot-notation keys flat
and literal. Serialization: the app hands the dictionary to SignalR (or another broadcaster) with that
broadcaster's JSON settings, so big integer markers and naming policy are the app's concern. Document this; if the
maintainer wants parity with page props, add an overload taking `IJsonSerializerOptionBuilder` that returns a
`JsonElement`.

### 5.6 Tests (new `LivePropTests.cs`, `BroadcastPropsTests.cs`)

Port the relevant cases from Laravel's `tests/LivePropTest.php` (1025 lines) and `tests/BroadcastPropsTest.php`
(332 lines); read them first and skip the ones about event classes, `broadcastOn` per connection, and DevTools.
At least:

Metadata shape
- `Inertia.Live(value, "order.updated", LiveChannel.Private("orders.1"))` → exact JSON from §5.2, no `throttle`.
- `throttle: 4000` → `"throttle": 4000`; two `Live` calls with throttles → the last one.
- Two events on one channel → one listener, two events; the same event twice → once.
- Two channels → two listeners in declaration order; a channel per declaration applies only to its events.
- Bare string channel → `public`; `private-orders.1` with `Private` → name `orders.1`; all four types serialize.
- `ILiveEvent` without a channel → its channels; with a channel → the given channel.
- No `liveProps` field when no prop is live.

Modifiers
- `.Live(...)` on optional, deferred, once, always props → entry emitted, prop keeps its other behavior.
- `Inertia.Defer(...).Merge().Live(...)` throws; `Inertia.Defer(...).Live(...).Merge()` throws; `MergeProp.Live` throws.
- Live with no event → throws when the page is built.
- String event without a channel → throws.

Resolver rules
- Full visit: deferred live prop not in `props`, listed in `deferredProps` **and** `liveProps`.
- Full visit: optional live prop not in `props`, in `liveProps`.
- Full visit: once live prop held by the client (`X-Inertia-Except-Once-Props`) → value skipped, `liveProps` entry kept.
- Partial reload `only: order` → `liveProps.order` emitted; `only: other` → not emitted.
- Partial reload with `except: order` → not emitted.
- Always live prop on a partial reload for another prop → emitted.
- Nested: `auth.order` live inside an anonymous object → key `auth.order`; partial `only: auth` → emitted
  (child of a resolved parent).

Socket id and broadcast
- `X-Socket-Id: 123.456` → `InertiaContext.SocketId == "123.456"`; absent → null.
- `BroadcastPropsAsync(new { order = … })` → `{ "__inertia": { "props": { "order": … } } }`; lazy delegate and
  wrappers resolved; nested containers walked; rescued deferred prop dropped; `"a.b"` key stays flat;
  `event`/`socketId` arguments fill the envelope and are omitted when null.

### 5.7 Transport-neutral core

Phase 3 adds nothing SignalR-specific to `Ponango.Inertia`: the metadata, `BroadcastPropsAsync` and `SocketId`
work with any broadcaster. SignalR support comes in Phases 4 and 5. Apps on a Pusher-protocol service (Pusher,
Ably, Soketi) can use the client's built-in `pusher()` transport with that service's .NET server SDK and
`BroadcastPropsAsync` for the payload; `docs/advanced-topics.md` gets a short note on that.

Every subscriber receives the same payload, so only broadcast values that are the same for everyone;
user-specific values should reload (send the event with no props).

### 5.8 Docs and bookkeeping

- `docs/compatibility.md`: `liveProps` ✅, `X-Socket-Id` ✅ (`InertiaContext.SocketId`), live props row in "Props
  and data", client rows for `useProp()` and the `live` router event ➖. Differences table: event and channel
  names are given as strings / `ILiveEvent` instead of derived from event classes.
- `README.md`: live props in the feature list and a short example.
- `requirements/protocol-requirements.md` (`liveProps` rules from §5.2/§5.5, `X-Socket-Id`),
  `requirements/public-api-requirements.md` (new API), `requirements/testing-and-docs-requirements.md` if it lists
  test files.
- `AGENTS.md`: capability list (live props), static factory list (`Inertia.Live`, `.Live()` modifier,
  `BroadcastPropsAsync`), `PropCapabilities` list (`ILiveProp`), `PropModifiers` (`LiveModifiers<TSelf>`), request
  headers (`X-Socket-Id`), `InertiaContext`/`InertiaController` (`SocketId`), test list.
- `CHANGELOG.md`: "Added" entries for each piece.

### 5.9 Suggested steps inside Phase 3

1. `LiveChannel`, `LiveBehavior`, `ILiveProp`, `LiveModifiers<TSelf>`, `LiveProp`, factories, merge conflict
   checks. Unit tests for listener building and modifier errors.
2. Resolver metadata, `PageModel.LiveProps`, `InertiaResult` wiring. Resolver tests.
3. `X-Socket-Id` header and `SocketId` properties.
4. `BroadcastPropsAsync`.
5. Docs, compatibility, AGENTS.md, CHANGELOG. (End-to-end checking happens in Phase 5, §7.5.)

---

## 6. Phase 4: `Ponango.Inertia.SignalR` (NuGet)

### 6.1 Layout

- `src/Ponango.Inertia.SignalR/Ponango.Inertia.SignalR.csproj`: `net8.0`, `FrameworkReference`
  `Microsoft.AspNetCore.App` (SignalR server is in the shared framework, so no extra package dependency),
  `ProjectReference` to `Ponango.Inertia` (becomes an exact-version package dependency when packed, D7). Copy the
  packaging metadata (icon, license, SourceLink, README) from `Ponango.Inertia.csproj`.
- `tests/Ponango.Inertia.SignalR.Tests/`: xUnit, `TestServer` plus `Microsoft.AspNetCore.SignalR.Client` for real
  hub connections in tests.
- Add both projects to `src/Ponango.Inertia/Ponango.Inertia.sln`.
- The package README is a short "Live props with SignalR" guide (server + client), linked from the main README.

### 6.2 Public API (proposed)

```csharp
// Registration
builder.Services.AddInertiaLive(options =>
{
    options.Channel("orders.{id}", (ClaimsPrincipal user, string id) => user.HasOrderAccess(id));   // private/presence rule
    options.Channel("team.{teamId}", async (HttpContext http, string teamId) => …);              // async overload
});
app.MapInertiaLive("/inertia/live");          // maps InertiaLiveHub, returns the HubEndpointConventionBuilder

// Sending
public interface IInertiaBroadcaster
{
    Task BroadcastAsync(ILiveEvent @event, object? props = null, string? exceptSocketId = null, CancellationToken ct = default);
    Task BroadcastAsync(string @event, IEnumerable<LiveChannel> channels, object? props = null, string? exceptSocketId = null, CancellationToken ct = default);
}

// In a controller, skipping the user who made the change:
await broadcaster.BroadcastAsync(new OrderUpdated(order), new { order }, exceptSocketId: SocketId);   // InertiaController.SocketId
```

- `options.Channel(pattern, rule)`: route-style patterns (`{name}` segments split on `.`), like Laravel's
  `Broadcast::channel('orders.{id}', …)`. Rules apply to `private` and `presence` channels; `public` channels are
  open; a private/presence channel with no matching rule is refused. Rule delegates get the user or the
  `HttpContext` plus the captured values as strings.
- `props` is optional: without props the client reloads the prop (the safe default for per-user values); with
  props it takes the value from the payload (same value for every subscriber).

### 6.3 Hub and wire contract (shared with Phase 5, keep in sync)

| Direction | Call | Notes |
|---|---|---|
| client → server | `Subscribe(type, name)` | Validates the type, runs the channel rule, then `Groups.AddToGroupAsync(connectionId, "{type}:{name}")`. Refusal throws `HubException("Unauthorized")` so the client can warn |
| client → server | `Unsubscribe(type, name)` | `RemoveFromGroupAsync`; never throws for an unknown group |
| server → client | `inertia(channelKey, envelope)` | `channelKey` is `"{type}:{name}"`; `envelope` is `BroadcastPropsAsync(props, @event, socketId)` |

- `encrypted-private` is refused (a Pusher feature, no SignalR equivalent). `presence` is authorized like
  `private`; live props never use presence member lists, so none are kept.
- Exclusion: `Clients.GroupExcept(group, exceptSocketId)` when a socket id is given. The envelope's `socketId` is
  not needed since SignalR excludes server-side; leave it out.
- Hub method name `inertia` and the `Subscribe`/`Unsubscribe` names are constants in one place in both packages.
- Scale-out (several servers) uses the app's SignalR backplane (Redis, Azure SignalR). Document; nothing to build.
- Authentication: the hub uses the app's normal auth (cookie auth works since the browser is same-origin). Document
  `.RequireAuthorization()` on `MapInertiaLive` for apps where only signed-in users may connect.

### 6.4 Tests

- Public channel: subscribe → receives `BroadcastAsync`; unsubscribe → no longer receives.
- Private channel: rule allows → receives; rule denies → `HubException`; no matching rule → refused.
- Pattern capture: `orders.{id}` passes `"1"` for `orders.1`; `orders.1.items` does not match.
- `encrypted-private` refused; unknown type refused.
- `exceptSocketId` → that connection receives nothing, others do.
- Envelope: `["private:orders.1", { "__inertia": { "event": "order.updated", "props": { … } } }]`.
- `ILiveEvent` overload sends to every channel the event lists.
- One test that the `ILiveEvent` used with `.Live(...)` and with `BroadcastAsync` yields matching `liveProps`
  listeners and channel keys (the drift this package exists to prevent).

### 6.5 Docs and bookkeeping

- Package README, a "Live props" section in `docs/advanced-topics.md` pointing at both packages, `README.md`
  feature list, `docs/compatibility.md` (live props ✅ with SignalR, or any Pusher-protocol service through the
  client's built-in `pusher()` transport with the app's own server SDK).
- `AGENTS.md`: new project, build/test commands for it, architecture section.
- `CHANGELOG.md`: "Added: `Ponango.Inertia.SignalR`".

---

## 7. Phase 5: `@ponango/inertia-signalr` (npm)

### 7.1 Layout and tooling

- `clients/inertia-signalr/`: `package.json`, `tsconfig.json`, `src/index.ts`, `test/`, `README.md`.
- TypeScript, ESM only (the Inertia packages are ESM only), built with `tsup` to `dist/` with `.d.ts`.
- Tests with `vitest` against a fake `HubConnection`.
- `peerDependencies`: `@inertiajs/core` `^3.11.0`, used for its types (`LiveOptions`, `LiveChannelTransport`,
  `LiveChannel`, `LiveEventHandler`). No dependency on `@microsoft/signalr`: the connection is typed by shape, the
  way Inertia's own `pusher()` and `actionCable()` transports avoid depending on their clients.
- `package.json` version is set by the release workflow from the tag (D7); keep `0.0.0-dev` in the repo.

### 7.2 API

```ts
import { HubConnectionBuilder } from '@microsoft/signalr'
import { signalR } from '@ponango/inertia-signalr'

const connection = new HubConnectionBuilder().withUrl('/inertia/live').withAutomaticReconnect().build()

createInertiaApp({
  live: signalR({ connection }),          // also accepts throttle and pauseWhenHidden, passed to Inertia
  // …
})
```

Options: `connection` (or a factory, resolved once and lazily), `start` (default `true`: the transport starts the
connection if it is `Disconnected`), `method` (default `'inertia'`), plus Inertia's `throttle` and
`pauseWhenHidden`.

### 7.3 Behavior (the transport implements `LiveChannelTransport`)

1. One `connection.on(method, (channelKey, payload) => …)` handler routes payloads to the handlers of that channel;
   Inertia reads `__inertia.event` itself.
2. `subscribeToChannel(channel, handler)`: ref-counted per `"{type}:{name}"`. The first handler invokes
   `Subscribe(type, name)` once the connection is `Connected` (queue until then); the last one leaving invokes
   `Unsubscribe` and ignores errors. Never throws (it runs during a page swap).
3. A channel released while its `Subscribe` is still in flight is unsubscribed when the call settles.
4. A refused `Subscribe` logs `Inertia could not subscribe to the live channel "<name>".` with the error, the same
   text as Inertia's own transports.
5. Reconnect: SignalR gives the connection a new id and drops its groups, so on `onreconnected` re-invoke
   `Subscribe` for every active channel, then report `connected`. `onreconnecting` and `onclose` report
   disconnected. `HubConnection` callbacks cannot be removed, so guard them with the current status callback.
6. `socketId: () => connection.connectionId ?? null` (changes after a reconnect; read per request, so that is fine).

### 7.4 Tests

- Routing: payload for `private:orders.1` reaches only that channel's handlers.
- Ref counting: two handlers → one `Subscribe`; removing one → no `Unsubscribe`; removing both → one `Unsubscribe`.
- Subscribe queued until connected; started when `start: true` and disconnected.
- Release during an in-flight `Subscribe` → `Unsubscribe` after it settles; no leaked group.
- Refused subscribe → warning, no throw.
- Reconnect → every active channel re-subscribed before `onStatusChange(true)`; `onreconnecting` → `false`.
- `socketId` reads the current `connectionId`.

### 7.5 End to end

A small sample app under `samples/LiveProps/` (ASP.NET Core + Vue or React, Vite) using all three packages through
project/workspace references: a page with a live `order` prop, a button that changes it from another tab, a
private channel with a rule. Checked by hand once (record it in the execution notes); an automated Playwright run
is optional and can come later.

### 7.6 Release tooling (D6, D7)

- `build.yml`: add a job that runs `npm ci`, `npm test` and `npm run build` in `clients/inertia-signalr`, and packs
  `Ponango.Inertia.SignalR` next to `Ponango.Inertia`.
- `release.yml`: on a `v*` tag, push both `.nupkg` files, then publish the npm package with the tag's version
  (`npm version "$VERSION" --no-git-tag-version`, then `npm publish --access public`, with `--tag next` for a
  pre-release so `latest` keeps pointing at a stable version). Use npm trusted publishing from GitHub Actions (OIDC)
  so no npm token is stored; npm only lets you set that up on a package that already exists, so the first publish
  is done by hand by the maintainer.
- `scripts/release.sh`: no change in the commands; update its header comment to say a tag publishes all three.
- Both registries are permanent for a given version; the existing candidate-build gate covers both.

### 7.7 Docs and bookkeeping

- `clients/inertia-signalr/README.md` (install, setup, the hub endpoint, troubleshooting a refused channel).
- `AGENTS.md`: the JS package, its build/test commands, and the rule that the hub contract in §6.3 changes in both
  packages together.
- `CHANGELOG.md`: "Added: `@ponango/inertia-signalr`".

---

## 8. Risks and open checks

- `HasStarted` as the "empty body" test (Phase 2) depends on the server; verify under `TestServer` and Kestrel.
- IIS and reverse proxies may reject `QUERY`; document, don't promise.
- Making `OnceProp` and `AlwaysProp` derive from a new base: confirm with the existing `ApiErgonomicsTests` that
  nothing public changes shape (no new required constructor arguments, same properties).
- The hub contract (§6.3) is shared by a C# and a TypeScript package; a mismatch breaks live props silently.
  Keep the names as constants in both, and keep the §6.4 envelope test and the §7.4 routing test asserting the
  same literal strings.
- Publishing to npm needs the `ponango` organization (D6) and a first manual publish before trusted publishing
  can be set up.
- `@inertiajs/core` may change the transport types in a minor release (the live API is new in 3.11). Pin the
  peer range to `^3.11.0` and re-check the types on each client release.

## 9. Execution notes

All phases on one branch, `feat/inertia-3.11-catch-up`, one commit per phase (the maintainer asked for a single
branch instead of one per phase).

- **Phase 1** (QUERY, shared once props, compatibility): no production code needed; every test passed as
  predicted. Verified by hand on Kestrel: `[AcceptVerbs("QUERY")]` with `[FromBody]` and `MapMethods(..., "QUERY")`
  both route and bind. The minimal-API example was left out of the docs because `InertiaResult` is an
  `IActionResult`, not an `IResult`. 190 tests.
- **Phase 2** (empty response): `HasStarted` is not used to detect an empty body, because response compression
  buffers small bodies further out. The middleware wraps the response body feature for Inertia requests
  (`ResponseWriteTracker`) and records whether bytes were written. Verified on Kestrel with Brotli compression:
  an empty `Ok()` becomes a 303 to the referer, a compressed `"0"` and a streamed body are left alone. One Phase 1
  test whose inner action wrote nothing was changed to render a page. 205 tests.
- **Phase 3** (live props): `throttle` is a `TimeSpan?` (sent as milliseconds). Unlike Laravel, `Append(path)` on a
  live deferred prop throws, since our `Append(path)` turns merging on (documented in the compatibility
  differences). `BroadcastPropsAsync` has a public overload taking an `ILogger` for rescued props. 255 tests.
- **Phase 4** (`Ponango.Inertia.SignalR`): the NuGet build and release steps (pack verification in `build.yml`,
  pack of both packages in `release.yml`) were done here instead of Phase 5, since they don't depend on npm.
  The package depends on `Ponango.Inertia` at the same version as a minimum (`>=`), the standard NuGet range,
  not an exact pin. Channel rules get a `LiveChannelAuthorizationContext` (user, captured values, HttpContext,
  connection id, scoped services) instead of positional delegate arguments. 23 SignalR tests over real hub
  connections (TestServer, long polling). To check at the first release: that nuget.org trusted publishing
  accepts the new package id from `release.yml`.
- **Phase 5** (`@ponango/inertia-signalr`): built with plain `tsc` instead of `tsup` (one file, nothing to bundle;
  TypeScript 7 is the current release). The transport reports the first connection too, because Inertia's live
  engine only treats a `connected` status as a reconnect after an earlier one; during a reconnect it holds the
  status until every channel is joined again. It polls the state when it did not start the connection itself
  (`start: false`, or a connection already `Connecting`), since `HubConnection` has no "connected" event. 25 vitest
  tests. The sample (`samples/LiveProps`, ASP.NET Core + Vue + Vite) is checked end to end by
  `ClientApp/e2e/run.mjs` in headless Edge (Chrome in CI): reload on event, value in event without a request,
  private channel refusal, `X-Socket-Id` sender exclusion, and rejoin plus refresh after a server restart, 6/6.
  Disabling the rejoin makes the restart check fail, so it is not vacuous. Found while writing the sample: inside
  an `InertiaController`, `Inertia.Live(...)` (like `Inertia.Defer(...)`) resolves to the controller's obsolete
  `Inertia<T>(...)` method. Fixed afterwards at the maintainer's request by removing that obsolete method (a
  member named `Inertia` always hides the type in derived classes, so no non-breaking fix exists); listed under
  breaking changes and in `docs/upgrading-to-3.0.md`. Release: `release.yml` packs the npm tarball on
  every run, publishes it on a tag (skipping a version already on npm), and attaches it to the GitHub Release;
  `build.yml` gained `client` and `e2e` jobs.
