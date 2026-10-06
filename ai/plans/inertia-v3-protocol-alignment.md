# Plan: Align Ponango.Inertia with the current Inertia.js v3 protocol

- **Status:** ready to execute
- **Written:** 2026-10-06, against `main` @ `9f608b2`
- **Target release:** `3.0.0` (some fixes change wire behavior, see [Versioning](#versioning-decision))
- **Executor:** an AI coding agent, one phase at a time

---

## 0. How to use this plan

### 0.1 Rules for the executing agent

1. Read `AGENTS.md`, then `README.md`, before you start. Both are authoritative for this repository.
2. Run the phases **in order**. Each phase leaves the build and every test green. Do not start a phase while
   the previous one is red.
3. Work on a branch: `git checkout -b feat/inertia-v3-alignment` (or one branch per phase, named
   `feat/v3-phase-N-<slug>`, if the maintainer wants smaller PRs).
4. **Test first.** Every behavior change below lists the test(s) that prove it. Write the test, watch it fail
   for the stated reason, then implement. The failing assertions in [§1.3](#13-verified-findings-probe-results)
   were actually observed on 2026-10-06, so a test that *passes* before you change code means the codebase has
   drifted and you should stop and re-check the item.
5. Each phase lists existing tests that **encode the old behavior** and must be updated. Update only those.
   If another existing test breaks, treat it as a regression and fix the code, not the test.
6. Per `AGENTS.md`: when request/response semantics change, update `README.md`, `docs/`, and `requirements/`
   **in the same phase**. Each phase lists its doc touch points. Add a line to the `CHANGELOG.md`
   `## 3.0.0 (unreleased)` section in every phase.
7. Keep the compatibility APIs (`LazyProp`, obsolete `Inertia(...)`, existing constructors and properties)
   unless a step explicitly says otherwise.
8. Match the surrounding code style: file-scoped namespaces in newer files, XML doc comments on public members,
   `ArgumentNullException.ThrowIfNull` guards, `StringComparer.Ordinal` for prop keys.
9. Commit at the end of each phase with a message like `Phase 1: fix once-prop expiry units and except-once
   handling`. Do not push or open PRs unless the maintainer asks.

### 0.2 Commands

> **Git Bash gotcha (verified):** the commands in `AGENTS.md` use `/p:` switches. Git Bash rewrites those as
> paths and MSBuild fails with `MSB1008: Only one project can be specified`. Use `-p:` / `-nr:` / `-v:` instead
> (PowerShell accepts both).

```bash
# Build library only
dotnet build src/Ponango.Inertia/Ponango.Inertia.csproj -p:GeneratePackageOnBuild=false -nr:false -v:minimal

# Full test suite (baseline on 2026-10-06: 35 passed, 0 failed, 7 CS0618 warnings from obsolete-API tests)
dotnet test tests/Ponango.Inertia.Tests/Ponango.Inertia.Tests.csproj -p:GeneratePackageOnBuild=false -nr:false -v:minimal

# A single test class while iterating
dotnet test tests/Ponango.Inertia.Tests/Ponango.Inertia.Tests.csproj -p:GeneratePackageOnBuild=false -nr:false -v:quiet \
  --filter "FullyQualifiedName~OncePropTests" --logger "console;verbosity=normal"

# Pack sanity check (final phase only)
dotnet pack src/Ponango.Inertia/Ponango.Inertia.csproj -c Release
```

### 0.3 Phase overview

| Phase | Theme | Size | Breaking? |
|---|---|---|---|
| 1 | Protocol correctness fixes (headers, escaping, errors, fragments, partial filters) | M | Yes (partial `except`, fragment redirects) |
| 2 | Flash data moves to top-level `page.flash` | S | **Yes** (wire shape) |
| 3 | Prop resolver rewrite and composable prop modifiers (once/defer/merge/optional/rescue) | L | No (additive; old ctors kept) |
| 4 | Infinite scroll and nested merge paths (`ScrollProp`, `Append(path)`) | M | No (`WithScroll` kept, gains `reset`) |
| 5 | Big integer support (`$bigint` markers) | M | No (opt-in) |
| 6 | Small options: all-errors-per-field, shared-key exposure, `ShareOnce`, lazy delegate props | S | No |
| 7 | *(optional, can ship later)* nested prop types and dot-notation partial reloads | L | No |
| 8 | Docs, changelog, requirements, final verification | S | n/a |

Phases 1, 2, 5 and 6 are mostly independent. Phase 4 depends on Phase 3 (it plugs `ScrollProp` into the new
resolver). Phase 7 depends on Phase 3.

---

## 1. Background

### 1.1 Sources of truth used

- Protocol spec: <https://inertiajs.com/docs/v3/core-concepts/the-protocol> (markdown at the same URL + `.md`)
- Feature pages: big-integers, once-props, deferred-props, merging-props, infinite-scroll, flash-data,
  partial-reloads, redirects, asset-versioning, validation, instant-visits (all under `/docs/v3/`)
- Reference server adapter: `inertiajs/inertia-laravel` branch `3.x`, in particular `src/PropsResolver.php`,
  `src/Middleware.php`, `src/Response.php`, `src/PreservesBigIntegers.php`, `src/ResolvesOnce.php`,
  `src/MergesProps.php`, `src/ScrollProp.php`, `src/DeferProp.php`. When this plan and the docs leave an edge
  case open, **do what `PropsResolver.php` does.**
- Client: `inertiajs/inertia` `v3.8.0` (2026-10-01) added BigInt revival (PR #3237, `packages/core/src/json.ts`).

### 1.2 Current architecture (what you will be editing)

| File | Role today |
|---|---|
| `src/Ponango.Inertia/InertiaResult.cs` | Builds the page object. `BuildPageModelAsync` + `ShouldIncludeProp` + `PassesPartialFilter` decide inclusion and metadata. `EvaluatePropsAsync` invokes wrappers. `EnsureProtocolFallback` does version checks when the middleware is absent. |
| `src/Ponango.Inertia/InertiaMiddleware.cs` | Vary header, version mismatch 409, shared data from options, external-redirect 409s, 302→303. |
| `src/Ponango.Inertia/InertiaContext.cs` | Headers, `Share`, `Flash`, `MergeFlashIntoSharedProps` (puts flash into `SharedProps["flash"]`). |
| `src/Ponango.Inertia/InertiaFlash.cs` | TempData-backed flash store (`__inertia_flash_{key}`, values JSON-serialized, read back as `JsonElement`). |
| `src/Ponango.Inertia/PageModel.cs` | Page object DTO and `ToJson` (builds **new** `JsonSerializerOptions` per call, camelCase, `WhenWritingNull`). |
| `src/Ponango.Inertia/HtmlHelperExtensions.cs` | `InertiaRender`: `<div id>` + `<script type="application/json" data-page="{appId}" data-inertia>`. |
| `src/Ponango.Inertia/PrecognitiveAttribute.cs` | Precognition 204/422 short-circuit. |
| `src/Ponango.Inertia/{Optional,Always,Deferred,Once,Merge,Lazy}Prop.cs` | Independent wrapper classes, each holding a `Func<Task<object>>`. No shared base. |
| `src/Ponango.Inertia/ScrollPropConfig.cs` | `PageName`, `int? PreviousPage`, `int? NextPage`, `int CurrentPage`. |
| `src/Ponango.Inertia/Inertia.cs` | Static factories `Optional/Always/Defer/Merge/Once`. |
| `src/Ponango.Inertia/InertiaOptions.cs` | `RootView`, `EncryptHistory`, `SharedData`, `JsonSerializerOptions`. |
| `tests/Ponango.Inertia.Tests/TestInfrastructure.cs` | `CreateContext(...)` builds DI + `DefaultHttpContext` (host `app.test`, https, TempData in `HttpContext.Items`, asset version `"test-version"`). `ReadJsonAsync(response)`. |

Useful test idiom (from existing tests). Reuse it in new test files:

```csharp
using var test = TestInfrastructure.CreateContext();
var http = test.HttpContext;
http.Request.Headers["X-Inertia"] = "true";
var inertia = test.GetRequiredService<InertiaContext>();
var result = inertia.Render("Page", new { });
result.Props["plans"] = Inertia.Once(() => "x", TimeSpan.FromHours(1));
await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(http));
using var doc = await TestInfrastructure.ReadJsonAsync(http.Response);
```

For HTML rendering tests, copy the `HtmlHelperProxy` (DispatchProxy) and `RenderHtml` helpers already in
`PlanCoverageTests.cs`.

### 1.3 Verified findings (probe results)

On 2026-10-06 a temporary probe file (`ZzSpecProbeTests.cs`, since deleted) asserted the **spec** behavior
for each item against the current code. Results:

| ID | Spec expectation | Observed today | Result | Phase |
|---|---|---|---|---|
| P01 | `onceProps.*.expiresAt` is a Unix timestamp in **milliseconds** | `1791292130` (seconds) | FAIL | 1 |
| P02 | Once prop skipped via `X-Inertia-Except-Once-Props` still gets its `onceProps` entry | entry dropped (no `onceProps` key at all) | FAIL | 1 (fixed in 3) |
| P03 | On a partial reload the except-once header is ignored, so a requested once prop resolves | prop omitted | FAIL | 1 (fixed in 3) |
| P04 | `Partial-Data: a,b` + `Partial-Except: b` → only `a` | `b` included (except ignored when data present) | FAIL | 1 |
| P05 | Flash at top-level `page.flash`, not in `props` | in `props.flash` | FAIL | 2 |
| P06 | `props.errors` is always present, defaults to `{}` | absent | FAIL | 1 |
| P07 | Internal redirect whose target has a `#fragment` → `409` + `X-Inertia-Redirect` | plain `302` passthrough | FAIL | 1 |
| P08 | External redirect with a fragment → `409` + `X-Inertia-Location` (client does `window.location`) | `X-Inertia-Redirect` (client would XHR another origin) | FAIL | 1 |
| P09 | Version-mismatch `409` echoes `X-Inertia-Version: <current>` | header missing | FAIL | 1 |
| P10 | Pending flash survives a version-mismatch `409` | survives (TempData not read) | PASS, **no work needed**, add a regression test | 1 |
| P11 | Script payload escapes every `/` as `\/` (and is safe with a relaxed encoder) | only exact lowercase `</script>` replaced; `</SCRIPT>` leaks with `UnsafeRelaxedJsonEscaping` | FAIL | 1 |
| P12 | Precognitive endpoints send `Vary: Precognition` | missing | FAIL | 1 |
| P13 | Partial reload with only `Partial-Except` resolves optional and deferred props not excluded (reference `PropsResolver`: `IgnoreFirstLoad` exclusion applies to non-partial requests only) | not resolved | FAIL | 1 |
| P14 | Scroll prop on reset request emits `scrollProps[key].reset = true` | no `reset` field | FAIL | 4 |
| P15 | Scroll prop merge label targets the inner array: `mergeProps: ["posts.data"]` | `["posts"]` | FAIL | 4 |
| P16 | Big integers become `{"$bigint":"…"}` when enabled | feature absent; emitted as raw number | (baseline) | 5 |
| P17 | Rescuable deferred prop that throws is omitted and listed in `rescuedProps` | exception fails the whole response | (baseline) | 3 |
| P18 | `sharedProps` lists `flash` today because flash rides in `SharedProps` | confirmed; goes away in Phase 2 | (baseline) | 2 |
| P19 | Prefetch redirects with fragments are left alone | 302 passthrough | PASS, keep it that way | 1 |
| P20 | Merge metadata is suppressed for a merge prop excluded by `Partial-Except` | suppressed | PASS, keep it that way | 3 |

Also confirmed by reading the code (no probe needed):

- `DeferredProp` cannot be combined with merge/once; `OptionalProp` and `MergeProp` cannot be combined with once.
  There is no `rescue`, `fresh`, or custom once key (`as`).
- `ScrollPropConfig` uses `int` pages, so cursor pagination (string cursors) cannot be expressed. Its `null`
  page values are **omitted** from JSON (class property + `WhenWritingNull`), whereas the spec example emits
  `"previousPage": null`.
- `X-Inertia-Infinite-Scroll-Merge-Intent` currently overrides append/prepend for **every** `MergeProp`. The
  reference applies it only to scroll props.
- There is no `rescuedProps`, `flash`, or `preserveBigIntegers` field on `PageModel`.

### 1.4 Versioning decision

Phases 1 and 2 change observable wire behavior:

- `props.flash` → `page.flash` (Phase 2)
- except-only partial reloads now resolve optional/deferred props (Phase 1, P13)
- external fragment redirects switch header; internal fragment redirects become 409s (Phase 1, P07/P08)
- `props.errors` is always present (Phase 1, P06)

All of these move toward what v3 clients expect. They still change behavior, so the release is **3.0.0**.
Record every one of them under `### Breaking changes` in `CHANGELOG.md`, and add a migration section (Phase 8).
The csproj `VersionPrefix` is only a local default. CI derives the real version from the git tag, so **do not**
edit csproj versions.

### 1.5 Out of scope (record in docs as "not supported yet", do not implement)

- SSR (POST page object to a Node render server, `head`/`body` slots, `/health`, `/shutdown`)
- The DevTools server protocol (`/docs/v3/advanced/devtools-protocol`)
- An Inertia-aware exception/error-page helper (Laravel `handleExceptionsUsing`)
- `ProvidesInertiaProperty` / `ProvidesInertiaProperties` interfaces
- "Store previous URL" (session concern specific to Laravel)
- Changing Precognition 422 error shapes (single string vs array). Leave as is.
- Removing `LazyProp` (v3 Laravel removed it; we keep it `[Obsolete]` for now)

---

## Phase 1: Protocol correctness fixes

**Goal:** fix every spec deviation that does not need the resolver redesign. Small, surgical edits.

New test file: `tests/Ponango.Inertia.Tests/ProtocolComplianceTests.cs` (one `[Fact]` per item below).

### 1.1 Once prop `expiresAt` in milliseconds (P01)

- File: `InertiaResult.cs`, `AddOncePropMetadata` (currently line ~403):
  `ToUnixTimeSeconds()` → `ToUnixTimeMilliseconds()`.
- Test `Once_prop_expiresAt_is_unix_milliseconds`: `Inertia.Once(() => "x", TimeSpan.FromHours(1))`, then assert
  `expiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` and `< now + 2h` in ms.
- Test: no expiry → `"expiresAt": null` is **present** (dictionary value, so `WhenWritingNull` does not drop it).
  Assert `ValueKind == Null`.

### 1.2 Combine `Partial-Data` and `Partial-Except` (P04)

- File: `InertiaResult.cs`, `PassesPartialFilter`. Apply `only` first, then remove `except`:
  ```csharp
  if (partialData.Count > 0 && !partialData.Contains(key)) return false;
  if (partialExcept.Contains(key)) return false;
  return true;
  ```
- Test `Partial_data_and_except_combine_with_except_winning`.

### 1.3 Except-only partial reloads resolve optional/deferred props (P13)

- Reference: in `PropsResolver::resolveProps`, `excludeFromInitialResponse` (which drops `IgnoreFirstLoad` =
  Optional + Defer) runs only when `!isPartial`. On a partial request the only gate is the only/except filter.
- File: `InertiaResult.cs`, `ShouldIncludeProp`:
  - `DeferredProp`: full visit → metadata + exclude (unchanged). Partial → `PassesPartialFilter(...)`.
  - `OptionalProp` / `LazyProp`: full visit → exclude. Partial → `PassesPartialFilter(...)`.
- **Update existing test** `InertiaResultTests.Partial_except_does_not_resolve_optional_or_deferred_props`.
  Rename it to `Partial_except_only_resolves_optional_and_deferred_props_not_excluded`, and assert the opposite:
  `stats` and `analytics` present, callbacks called once each, `users` absent.
- Add a test that a full visit still excludes both and calls neither callback (this is already partly covered in
  `PlanCoverageTests.Optional_and_always_props_follow_partial_reload_rules`. Leave that test as is).
- Breaking-change note for the changelog: "`router.reload({ except: [...] })` now also resolves optional and
  deferred props that are not excluded, matching the reference adapter."

### 1.4 `errors` always present, defaulting to `{}` (P06)

- File: `InertiaResult.cs`, `BuildPageModelAsync`. After merging shared props and before the include loop:
  if `props` has no `"errors"` key, add `props["errors"] = new Dictionary<string, object>()`.
  Do not add `"errors"` to `sharedPropKeys`.
- Errors are always-included (existing `key == "errors"` checks), so partial reloads keep them.
- Test `Errors_prop_defaults_to_empty_object_on_full_and_partial_visits` (both modes, `ValueKind == Object`,
  zero properties).
- Check that the existing `With_errors_supports_flat_explicit_bag_and_header_bag_shapes` and
  `Partial_reloads_preserve_errors_even_when_not_requested` still pass unchanged.

### 1.5 Fragment redirects (P07, P08, P19)

Reference (`Middleware.php`): for Inertia requests, after 302→303 conversion,
`if (isRedirect && Location contains '#' && !prefetch) → 409 + X-Inertia-Redirect: <Location>`.
Laravel has no automatic external-redirect detection; we do. An external target can never be fetched via XHR,
so for us **external always wins and uses `X-Inertia-Location`**.

- File: `InertiaMiddleware.cs`, post-`_next` block. New order:
  1. `if (!IsInertia || Response.HasStarted || !IsRedirectStatus) → skip`.
  2. External (existing `IsExternalUrl`) → `409`, remove `Location`, set `X-Inertia-Location: <location>`
     (**fragment or not**).
  3. Else if the location contains `#` and the request is **not** a prefetch (`inertiaContext.IsPrefetch`) → `409`,
     remove `Location`, set `X-Inertia-Redirect: <location as-is>`.
  4. Else 302→303 for non-GET (unchanged).
- **Update existing test** `InertiaMiddlewareTests.External_redirect_with_fragment_uses_redirect_header`. Rename it to
  `External_redirect_with_fragment_uses_location_header`, and assert `X-Inertia-Location` equals the full URL
  including `#done`, with no `X-Inertia-Redirect`.
- New tests:
  - `Internal_redirect_with_fragment_returns_409_with_redirect_header` (GET, `Location: /article/new#section`).
  - `Internal_redirect_with_fragment_after_post_returns_409` (POST + 302 → 409, not 303).
  - `Prefetch_redirect_with_fragment_is_not_converted` (`Purpose: prefetch` → stays 302).
  - `Non_inertia_redirect_with_fragment_is_not_converted` (no `X-Inertia` header → 302).
- `InertiaController.Redirect` → `InertiaRedirectResult` turns local URLs into absolute same-host URLs, so
  `IsExternalUrl` is false and the fragment rule applies. Add a test that runs an `InertiaRedirectResult`
  (via a controller, or by constructing it in the test; it's `internal`, so use the controller path) to a
  `/x#y` URL through the middleware → 409 `X-Inertia-Redirect`.
- Docs: `docs/advanced-topics.md` § External redirects (line ~229),
  `requirements/protocol-requirements.md` § External redirects (line ~140 currently *requires* the old behavior,
  so rewrite it).

### 1.6 Echo `X-Inertia-Version` on version-mismatch 409 (P09, P10)

- `InertiaMiddleware.cs` mismatch branch: add `context.Response.Headers["X-Inertia-Version"] = currentVersion;`.
- `InertiaResult.cs` `EnsureProtocolFallback` mismatch branch: add `X-Inertia-Version = AssetsVersion`.
- Tests: extend `PlanCoverageTests.Middleware_returns_conflict_when_asset_version_mismatches` with the header
  assertion, and add a fallback-path test (no middleware, `InertiaResult` directly with a stale version).
- Regression test `Version_mismatch_keeps_pending_flash_for_follow_up_request`: call `inertia.Flash("m","x")`,
  run the middleware with a stale version, and assert the TempData key `__inertia_flash_m` is still present.
  This already passes; it locks the behavior in.

### 1.7 Script payload escaping (P11)

- Spec: the embedded JSON MUST escape every `/` as `\/`, and MUST NOT HTML-entity encode. The client's own SSR
  (`buildSSRBody`) does `.replace(/\//g, '\\/').replace(/</g, '\\u003c')`, so do the same.
- File: `HtmlHelperExtensions.cs`: replace `json.Replace("</script>", "<\\/script>")` with
  ```csharp
  var safeJson = json.Replace("/", "\\/").Replace("<", "\\u003c");
  ```
  This is safe: in JSON text `/` and `<` can only occur inside string literals, and `\/` and `<` are valid
  escapes there. System.Text.Json never emits a bare `\` before `/`.
- **Update existing test** `PlanCoverageTests.Inertia_render_emits_v3_script_payload_and_escapes_script_end_tags`.
  Its `TestSerializerOptionsBuilder` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so today the payload has
  a raw `</script>` that gets replaced with `<\/script>`. After the change it becomes `<\/script>`, so the
  current `Assert.Contains("<\\/script>", html)` fails. Replace it with: extract the text between the
  `<script ...>` open tag and the final `</script>`, assert `!body.Contains("</")`, assert
  `body.Contains("\\/users")`, and assert `JsonDocument.Parse(body)` succeeds with
  `props.unsafeValue == "</script><p>bad</p>"` and `url == "/users"`. (With the *default* encoder, System.Text.Json
  already writes `<` as `<`. The new replace leaves that untouched, which is fine.)
- New test `Script_payload_is_safe_with_relaxed_encoder`: an `IJsonSerializerOptionBuilder` that sets
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, a prop value `"</SCRIPT><b>"`, then assert that no `</` is in the
  body and the JSON parses back to the original string.

### 1.8 `Vary: Precognition` (P12)

- Spec: "Set to `Precognition` on all responses when the Precognition middleware is applied."
- Refactor `InertiaMiddleware.EnsureVaryHeader(IHeaderDictionary)` into
  `internal static void AppendVary(IHeaderDictionary headers, string value)` (case-insensitive de-dupe,
  `", "` join). Keep `EnsureVaryHeader(headers)` as a one-line wrapper calling `AppendVary(headers, "X-Inertia")`,
  because `PlanCoverageTests.Vary_header_helper_appends_without_overwriting_existing_values` reflects on it by name.
- `PrecognitiveAttribute.OnActionExecuting`: call `InertiaMiddleware.AppendVary(Response.Headers, "Precognition")`
  as the **first** statement, before the `IsPrecognition` early return, so normal responses vary too.
- Tests (in `PrecognitionTests.cs`): precognitive request → `Vary` contains `Precognition`; non-precognitive
  request on the same attribute → `Vary` contains `Precognition`; existing `Accept-Encoding` is preserved.

### Phase 1 exit criteria

- All new tests pass, the 4 updated tests pass, and the rest of the suite is unchanged.
- Docs updated: `docs/advanced-topics.md` (partial reloads, external redirects), `requirements/protocol-requirements.md`
  (partial reload behavior, external redirects, asset version handling, initial HTML response escaping),
  `README.md` § How It Works if it mentions any of these.
- `CHANGELOG.md` has a `## 3.0.0 (unreleased)` section with `### Fixed` and `### Breaking changes` entries.

---

## Phase 2: Flash data as top-level `page.flash` (P05, P18)

**Goal:** match v3, where flash is a page-object field. The client exposes it as `page.flash`, fires
`inertia:flash` / `onFlash`, and **strips it from history state** (it no longer reappears on Back).

Reference (`Response::toResponse`): `flash` is added for both JSON and HTML responses, only when non-empty, and
it is **not** a prop and **not** a shared prop.

### Steps

1. `PageModel.cs`: add
   ```csharp
   // Flash data for this response (v3 - only serialized when present; not persisted in client history)
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public IDictionary<string, object?>? Flash { get; set; }
   ```
2. `InertiaContext.cs`:
   - Remove `MergeFlashIntoSharedProps()` and its helper `TryConvertToDictionary`.
   - Add `internal IDictionary<string, object?> PullFlash()`, which returns `flash?.ReadAll()` (or an empty dictionary).
     Values written via `Flash(...)` during the **current** request must be included too. `InertiaFlash.Flash`
     writes to TempData and `ReadAll` reads TempData, so this already holds. Keep a test for it.
   - Update the XML docs on `Flash(...)` to say "page.flash" instead of "flash shared prop".
3. `InertiaResult.ExecuteResultAsync`: delete the `MergeFlashIntoSharedProps()` call. In `BuildPageModelAsync`
   (both HTML and JSON paths): `var flash = InertiaContext.PullFlash(); if (flash.Count > 0) pageModel.Flash = flash;`
   Pull flash **once** per result execution, at the point where the page is built (not in the fallback-409 path,
   so flash survives a mismatch exactly like the middleware path).
4. A user who calls `inertia.Share("flash", ...)` now just has an ordinary shared prop named `flash` in `props`.
   No special merging. Document it in the migration notes.
5. `InertiaResult.WithFlash` stays. Also add `public InertiaResult WithFlash(IDictionary<string, object?> values)`
   for multiple values (mirrors `Inertia::flash([...])`).

### Tests

- **Update** `InertiaResultTests.Flash_is_merged_with_existing_shared_flash_values`. Rename it to
  `Flash_is_emitted_at_page_level_and_shared_flash_prop_is_independent`: `Share("flash", {existing})` stays in
  `props.flash.existing`, while `Flash("success")` appears at `root.flash.success` and not in props.
- **Update** `PlanCoverageTests.Flash_values_are_emitted_once_and_merge_with_object_shaped_flash`. Rename it to
  `Flash_values_are_emitted_once_at_page_level`: the first response has `root.flash.success`, and the second
  response has no `flash` property at the root.
- **Update** `ApiErgonomicsTests.Render_uses_top_level_props_and_fluent_with_helpers`: assert
  `root.flash.success == "created"` instead of `props.flash.success`.
- New: `Flash_is_included_in_initial_html_page_model` (non-Inertia request; capture the view model like
  `Root_view_option_is_used_unless_view_name_is_explicit` does with `CapturingViewResultExecutor`, and assert
  `((PageModel)model).Flash["k"]`).
- New: `Flash_is_not_listed_in_sharedProps` (P18 reversed).
- New: `Flash_survives_redirect_until_rendered`: flash, then a redirect result executes (no `InertiaResult`), then a
  later `InertiaResult` on the same TempData store emits it.

### Docs

`README.md` (Features list line ~66, How It Works line ~138), `docs/advanced-topics.md` § Flash messages
(show `usePage().flash`, `router.on('flash')`), `docs/migration-from-v1.md` § Shared props and flash data,
`requirements/protocol-requirements.md` (core response model: add `flash`), and
`requirements/testing-and-docs-requirements.md` ("flash merging" → "flash at page level").
CHANGELOG `### Breaking changes`: "Flash data is now emitted as the top-level `flash` page field (read it with
`usePage().flash` or the `flash` event) instead of `props.flash`."

---

## Phase 3: Prop resolver rewrite and composable modifiers

**Goal:** model prop behavior the way the reference does, as **capabilities** that can be combined
(deferred + merge, deferred + once, merge + once, optional + once, deferred + rescue). Then replace the ad-hoc
inclusion logic with one resolver that follows `PropsResolver.php`. This also closes P02, P03 and P17.

### 3.1 Capability interfaces (new file `src/Ponango.Inertia/PropCapabilities.cs`, all `public`)

```csharp
namespace Ponango.Inertia;

/// Marker: never resolved on a full (non-partial) visit. Implemented by OptionalProp, DeferredProp, LazyProp.
public interface IIgnoreFirstLoad { }

public interface IDeferrableProp
{
    bool ShouldDefer { get; }
    string Group { get; }
}

public interface IMergeableProp
{
    bool ShouldMerge { get; }
    bool ShouldDeepMerge { get; }
    /// true = append at root, false = prepend at root (only meaningful when no paths are set)
    bool AppendsAtRoot { get; }
    bool PrependsAtRoot { get; }
    IReadOnlyList<string> AppendPaths { get; }
    IReadOnlyList<string> PrependPaths { get; }
    // Named MatchOnPaths (not MatchOn) because MergeProp already exposes a public `string? MatchOn` property.
    IReadOnlyList<string> MatchOnPaths { get; }
}

public interface IOnceableProp
{
    bool ShouldResolveOnce { get; }
    bool ShouldBeRefreshed { get; }   // .Fresh()
    string? OnceKey { get; }          // .As("roles")
    long? ExpiresAtMilliseconds();    // null = never
}

public interface IRescuableProp
{
    bool ShouldRescue { get; }
}

/// Anything with a lazily-invoked value.
public interface IResolvableProp
{
    Task<object?> ResolveAsync();
}
```

### 3.2 Shared state helpers (internal, new file `src/Ponango.Inertia/PropBehaviors.cs`)

- `internal sealed class MergeBehavior` mirrors `MergesProps.php`: fields `merge`, `deepMerge`, `append = true`,
  `List<string> appendPaths`, `prependPaths`, `matchOn`. Methods `Merge()`, `DeepMerge()`,
  `Append(bool)`, `Append(string path, string? matchOn = null)`, `Append(IDictionary<string,string?> pathsToMatchOn)`,
  `Prepend(...)` (same overloads), `SetMatchOn(params string[])`. `AppendsAtRoot => append && no paths`,
  `PrependsAtRoot => !append && no paths`. Path-level `matchOn` is stored as `"{path}.{matchOn}"`
  (exactly as `MergesProps::append`).
- `internal sealed class OnceBehavior` mirrors `ResolvesOnce.php`: `once`, `refresh`, `key`, `TimeSpan? ttl`,
  `DateTimeOffset? until`. `ExpiresAtMilliseconds()` = `(until ?? now + ttl)?.ToUnixTimeMilliseconds()`.
  Compute it at **metadata time** (once per response), not at construction.

### 3.3 Rework the wrapper classes (keep every existing public ctor, property and method)

Each class keeps its `Func<Task<object>>` callback, its `InvokeAsync()`/`Invoke()`, and implements
`IResolvableProp`. Fluent methods return the concrete type (`this`). The C# surface:

| Class | Implements | New fluent API |
|---|---|---|
| `OptionalProp` | `IIgnoreFirstLoad`, `IOnceableProp` | `Once(bool = true, string? @as = null, TimeSpan? until = null)`, `As(string)`, `Fresh(bool = true)`, `Until(TimeSpan)`, `Until(DateTimeOffset)` |
| `DeferredProp` | `IIgnoreFirstLoad`, `IDeferrableProp` (`ShouldDefer => true`), `IMergeableProp`, `IOnceableProp`, `IRescuableProp` | `Merge()`, `DeepMerge()`, `Append(...)`, `Prepend(...)`, `MatchOn(params string[])`, `Once(...)`, `As`, `Fresh`, `Until`, `Rescue(bool = true)`; new ctor overloads `DeferredProp(Func<object> cb, string group = "default", bool rescue = false)` (add the `rescue` param as optional so existing calls still bind) |
| `MergeProp` | `IMergeableProp`, `IOnceableProp` | `Append(...)`, `Prepend(...)`, `MatchOn(...)`, `DeepMerge()`, `Once(...)`, `As`, `Fresh`, `Until`. Existing ctor `(cb, MergeMode mode, string? matchOn)` maps `Append` → root append, `Prepend` → root prepend, `DeepMerge` → deepMerge; `matchOn` → `MatchOn(matchOn)`. Keep `Mode`, `MatchOn` (string?) properties as they are; they reflect the ctor args. Keep `WithScroll(...)` (see Phase 4). |
| `OnceProp` | `IOnceableProp` (`ShouldResolveOnce => true`) | `As`, `Fresh`, `Until`. Keep `ExpiresAfter`. Ctor `expiresAfter` → `Until(expiresAfter)`. |
| `AlwaysProp` | `IResolvableProp` only | (none) |
| `LazyProp` (obsolete) | `IIgnoreFirstLoad` | (none) |

Naming clash: `MergeProp` already has a read-only `MatchOn` **property** (`string?`). A method named `MatchOn(...)`
cannot coexist with it. Name the fluent method **`MatchingOn(params string[] fields)`** on all classes and leave
the property alone. Use the same name everywhere for consistency, and note it in the docs.

Static factory additions in `Inertia.cs`, all keeping the existing ones:

```csharp
public static DeferredProp Defer(Func<object> cb, string group = "default", bool rescue = false)
public static DeferredProp Defer(Func<Task<object>> cb, string group = "default", bool rescue = false)
public static MergeProp DeepMerge(Func<object> cb)                       // = new MergeProp(cb, MergeMode.DeepMerge)
public static MergeProp DeepMerge(Func<Task<object>> cb)
```

Changing `Defer`'s signature by adding an optional parameter is source-compatible but **binary-breaking**. That is
fine for 3.0.0. Note it in the changelog.

### 3.4 New internal resolver (`src/Ponango.Inertia/PropsResolver.cs`)

Port `PropsResolver.php`, **top level only** in this phase (nested props are Phase 7). Write the path helpers
(`MatchesOnly`, `LeadsToOnly`, `MatchesExcept`) path-aware now, so Phase 7 only adds recursion.

```csharp
internal sealed class PropsResolver
{
    // inputs
    public PropsResolver(InertiaRequestHeaders headers, bool isInertia, string component, ILogger logger, InertiaOptions? options);
    // state: isPartial, only (HashSet or null), except (HashSet or null), resetProps, loadedOnceProps
    // outputs (metadata):
    //   Dictionary<string, List<string>> DeferredProps; List<string> RescuedProps, MergeProps, PrependProps,
    //   DeepMergeProps, MatchPropsOn; Dictionary<string, object?> ScrollProps; Dictionary<string, object?> OnceProps
    public async Task<Dictionary<string, object?>> ResolveAsync(IDictionary<string, object?> props);
}
```

- `isPartial = isInertia && headers.PartialComponent == component`
- `only` / `except` = parsed CSV, **null when the header is absent or empty** (that distinction matters, as in
  `parseHeader`)

Per top-level `(key, value)`, in order:

1. `path = key`.
2. **Partial filter:** if `isPartial && value is not AlwaysProp && key != "errors"` and
   `!PathMatchesPartialRequest(path)` → skip. (`PathMatchesPartialRequest`: if `only != null` and neither
   `MatchesOnly(path)` nor `LeadsToOnly(path)` → false; if `except != null && MatchesExcept(path)` → false.)
3. **Initial-load exclusion** (only when `!isPartial`), `ExcludeFromInitialResponse(value, path)`:
   - `IIgnoreFirstLoad` → collect deferred metadata if `IDeferrableProp{ShouldDefer}` **and not**
     `WasAlreadyLoadedByClient`; collect merge metadata if `IMergeableProp{ShouldMerge}`; collect once metadata if
     `IOnceableProp{ShouldResolveOnce}`. → exclude.
   - `IDeferrableProp{ShouldDefer}` (scroll props with `.Defer()`, Phase 4) → deferred + merge metadata. → exclude.
     **No** scroll metadata (spec: "A deferred scroll prop emits no scrollProps on the full visit").
   - `isInertia && WasAlreadyLoadedByClient(value, path)` → collect once metadata → exclude. (Closes **P02**.)
4. **Resolve** `IResolvableProp` values with `await ResolveAsync()`. Wrap the call in try/catch: on exception, if
   `value is IRescuableProp{ShouldRescue}` → `logger.LogError(ex, "Inertia deferred prop '{Prop}' failed and was rescued", path)`,
   add `path` to `RescuedProps`, skip the prop (do **not** emit `null`). Otherwise rethrow. (Closes **P17**.)
5. **Unwrap one level:** if the resolved value is itself a prop type (implements any capability or is
   `AlwaysProp`), re-run step 3 for it (when `!isPartial`), then resolve it. This mirrors the "closure returned a
   prop type" branch.
6. **Collect metadata** for included props: merge (if `ShouldMerge`), scroll (Phase 4), once (if `ShouldResolveOnce`).
7. Emit `result[key] = resolvedValue`.

Metadata helpers, copied from the reference:

- `CollectMergeable(path, p)`: return if `resetProps.Contains(path)`; return if
  `isPartial && !IsIncludedInPartialMetadata(path)`; then `DeepMerge` → `deepMergeProps += path`; else
  `AppendsAtRoot` → `mergeProps += path`; else `PrependsAtRoot` → `prependProps += path`; else
  `mergeProps += path.appendPath` for each append path and `prependProps += path.prependPath` for each prepend path.
  Then `matchPropsOn += $"{path}.{m}"` for each `m` in `MatchOnPaths`.
- `CollectOnce(path, p)`: skip if `isPartial && !IsIncludedInPartialMetadata(path)`;
  `onceProps[p.OnceKey ?? path] = { "prop": path, "expiresAt": p.ExpiresAtMilliseconds() }` (dictionary so `null`
  is serialized).
- `WasAlreadyLoadedByClient(p, path)`: `p is IOnceableProp{ShouldResolveOnce, !ShouldBeRefreshed}` and
  `loadedOnceProps.Contains(p.OnceKey ?? path)`.
- Partial reloads **never** consult `loadedOnceProps` (step 3 only runs when `!isPartial`). (Closes **P03**.)
- `IsIncludedInPartialMetadata(path)`: (`only == null || MatchesOnly(path)`) && (`except == null || !MatchesExcept(path)`).

Merge-intent header (`X-Inertia-Infinite-Scroll-Merge-Intent`): **remove** the global override
(`GetEffectiveMergeMode`) from plain `MergeProp`s. Apply it only to scroll props (Phase 4) and to legacy
`MergeProp`s that have a `ScrollConfig` (to preserve today's scroll behavior). Add a test showing that a plain
`MergeProp` ignores the header. Changelog it under Changed.

### 3.5 Wire the resolver into `InertiaResult`

- `BuildPageModelAsync`: keep shared-prop merge + `sharedPropKeys` bookkeeping + the `errors` default
  (Phase 1). Then `var resolved = await new PropsResolver(...).ResolveAsync(props);` and copy every non-empty
  metadata collection onto `PageModel`. Remove a key from `sharedPropKeys` when it is absent from `resolved`
  (today's behavior: `sharedProps` reflects emitted keys only).
- Delete `ShouldIncludeProp`, `PassesPartialFilter`, `AddDeferredPropMetadata`, `AddOncePropMetadata`,
  `GetEffectiveMergeMode`, `EvaluatePropsAsync`. Keep `ParseCommaSeparated` (move it into the resolver).
- `PageModel.cs`: add
  ```csharp
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? RescuedProps { get; set; }
  ```
  Change `OnceProps`/`ScrollProps` value types to `Dictionary<string, object?>` if needed so `null`s serialize.
- Logger: resolve `ILoggerFactory` from `HttpContext.RequestServices` (category `"Ponango.Inertia"`). Fall back to
  `NullLogger.Instance` when it is absent. `Microsoft.Extensions.Logging` comes in through the ASP.NET Core
  framework reference, so no package change is needed.

### 3.6 `InertiaContext.ShareOnce`

`public void ShareOnce(string key, Func<object> callback)` (and async overload) =
`Share(key, new OnceProp(callback))`. Return the `OnceProp` so callers can chain `.As(...)`/`.Until(...)`.
Shared once props then flow through the resolver like any other prop.

### Tests: new file `tests/Ponango.Inertia.Tests/PropsResolverTests.cs`

Write each as its own `[Fact]`. "full" = Inertia request without partial headers; "partial" = component-matching
partial request.

Once:

1. `Except_once_header_skips_value_but_keeps_onceProps_metadata` (P02). The callback is not invoked.
2. `Partial_reload_resolves_once_prop_even_if_listed_in_except_once` (P03).
3. `Fresh_once_prop_is_resolved_even_when_client_has_it`.
4. `Once_custom_key_is_used_for_metadata_and_except_matching`: `Inertia.Once(...).As("roles")` on prop
   `memberRoles` → `onceProps.roles = { prop: "memberRoles", ... }`; `Except-Once-Props: roles` skips it.
5. `Once_until_datetime_sets_expiresAt_in_ms`.
6. `Html_visit_ignores_except_once_header` (`isInertia == false`).
7. `Shared_once_prop_via_ShareOnce_is_cached_like_page_once_props`.

Deferred composition:

8. `Deferred_merge_prop_announces_deferred_and_merge_metadata_on_full_visit` (`Inertia.Defer(...).DeepMerge()` →
   `deferredProps.default` + `deepMergeProps` contain the key; `props` doesn't).
9. `Deferred_merge_prop_emits_merge_metadata_when_loaded`.
10. `Deferred_once_prop_already_loaded_is_not_announced_as_deferred_but_keeps_once_metadata`.
11. `Optional_once_prop_emits_once_metadata_on_full_visit_without_resolving`.
12. `Merge_once_prop_emits_both_metadata`.

Rescue:

13. `Rescued_deferred_prop_is_omitted_and_listed_in_rescuedProps` (partial, throwing callback, `rescue: true`; status
    200, other props intact, `rescuedProps == ["perm"]`, `props` has no `perm`, and a logged error is captured via a
    test `ILoggerProvider`).
14. `Unrescued_deferred_prop_exception_propagates` (P17 baseline stays true without rescue).

Merge paths:

15. `Merge_append_at_path_emits_nested_label`: `Inertia.Merge(...).Append("data", matchOn: "id")` →
    `mergeProps ["users.data"]`, `matchPropsOn ["users.data.id"]`.
16. `Merge_append_multiple_paths_with_match_fields` (dictionary overload).
17. `Merge_prepend_at_path`.
18. `Reset_header_suppresses_merge_metadata_but_returns_value`.
19. `Merge_metadata_omitted_for_partial_excluded_prop` (P20 regression).
20. `Plain_merge_prop_ignores_infinite_scroll_merge_intent_header`.

Regression: all existing `PlanCoverageTests` prop tests, `Lazy_prop_and_async_wrapper_callbacks_resolve_when_requested`,
and `Shared_props_metadata_reflects_final_emitted_shared_keys` stay green **unchanged**. **Update**
`PlanCoverageTests.Once_props_emit_metadata_and_respect_except_header`: its last assertion
`Assert.False(second.RootElement.TryGetProperty("onceProps", out _))` must become "`onceProps.plans` present,
`props.plans` absent, `calls == 1`".

### Docs

`docs/advanced-topics.md` § Prop wrappers: add "Combining prop types", "Rescuing deferred props", once options
(`As`, `Fresh`, `Until`, `ShareOnce`), and merge paths (`Append(path, matchOn)`). `requirements/protocol-requirements.md`
§ Prop wrapper requirements + Partial reload behavior. `requirements/public-api-requirements.md` § Static factory
helpers. `AGENTS.md` § Prop Wrapper Semantics (keep it accurate).

---

## Phase 4: Infinite scroll (`ScrollProp`) and scroll metadata fixes (P14, P15)

**Goal:** match `Inertia::scroll()`. The inner wrapper array gets the merge label (`posts.data`), page values may
be ints or strings (cursors), metadata nulls are emitted, a `reset` flag is sent, the merge-intent header decides
append vs prepend, and the prop can optionally be deferred.

### Steps

1. New `src/Ponango.Inertia/ScrollMetadata.cs`:
   ```csharp
   public sealed class ScrollMetadata
   {
       public string PageName { get; }
       public object? PreviousPage { get; }   // int, long or string (cursor)
       public object? NextPage { get; }
       public object? CurrentPage { get; }
       public static ScrollMetadata ForPage(int currentPage, int? previousPage, int? nextPage, string pageName = "page");
       public static ScrollMetadata ForCursor(string? currentCursor, string? previousCursor, string? nextCursor, string cursorName = "cursor");
       internal Dictionary<string, object?> ToDictionary(bool reset)
           => new() { ["pageName"] = PageName, ["previousPage"] = PreviousPage, ["nextPage"] = NextPage,
                      ["currentPage"] = CurrentPage, ["reset"] = reset };
   }
   ```
2. New `src/Ponango.Inertia/ScrollProp.cs`, implementing `IResolvableProp`, `IMergeableProp`, `IDeferrableProp`:
   - ctor `(Func<object> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")`, plus async and
     `ScrollMetadata`-instance overloads.
   - Always merges (`ShouldMerge => true`). Before metadata collection the resolver calls
     `ConfigureMergeIntent(headers.MergeIntent)`: `"prepend"` → prepend at `wrapper`, otherwise append at `wrapper`
     (as in `ScrollProp::configureMergeIntent`). Reset the path lists before each configuration so a reused
     instance does not accumulate paths.
   - `Defer(string group = "default")` sets `ShouldDefer`. Default is not deferred.
   - Caches the resolved value (the metadata callback receives it; resolve once).
   - `MatchingOn(...)` like other mergeables (e.g. `"id"` → `posts.data.id`; implement as
     `matchOn = $"{wrapper}.{field}"` if the caller passes a bare field. Document it.)
3. `Inertia.cs`: `public static ScrollProp Scroll(Func<object> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")`,
   plus overloads taking `ScrollMetadata` directly and async callbacks.
4. Resolver (Phase 3) `CollectScroll(path, prop)`: `scrollProps[path] = prop.Metadata.ToDictionary(reset: resetProps.Contains(path))`.
   It runs on included props only (so never for a deferred scroll prop on the full visit).
5. Legacy `MergeProp.WithScroll(...)`: keep it working and mark it
   `[Obsolete("Use Inertia.Scroll(...) instead.")]`. Its scroll metadata also gains `reset` and explicit nulls
   (convert `ScrollPropConfig` → dictionary in the resolver). Keep its merge label at the **root key** (today's
   behavior) so existing apps don't change shape. The new API is where `posts.data` applies. Keep the merge-intent
   override for it (Phase 3 note).

### Tests: new file `tests/Ponango.Inertia.Tests/ScrollPropTests.cs`

1. `Scroll_prop_full_visit_labels_inner_wrapper_and_emits_metadata` (P15): `mergeProps == ["posts.data"]`,
   `scrollProps.posts == {pageName:"page", previousPage:null, nextPage:2, currentPage:1, reset:false}`. Assert
   `previousPage` is present with `ValueKind.Null`.
2. `Scroll_prop_reset_request_sets_reset_true_and_drops_merge_label` (P14): partial + `X-Inertia-Reset: posts`.
   Note that `reset` matches the **prop path** `posts` while the merge label is `posts.data`. Per the reference,
   `resetProps` is compared against `path` (`posts`), so the label is suppressed. Assert that no `mergeProps` contain
   `posts.data`.
3. `Scroll_prop_prepend_intent_uses_prependProps`.
4. `Scroll_prop_cursor_metadata_emits_strings`.
5. `Deferred_scroll_prop_full_visit_announces_deferred_and_merge_without_scrollProps`.
6. `Custom_wrapper_key_is_used_in_merge_label` (`wrapper: "items"` → `posts.items`).
7. `Legacy_WithScroll_emits_reset_and_explicit_nulls` (the `MergeProp.WithScroll` path; existing label kept).

### Docs

`docs/advanced-topics.md` § Infinite scroll: rewrite around `Inertia.Scroll(...)`, show page and cursor
examples, multiple scroll containers via `pageName`, and `.Defer()`. Add a migration note from `WithScroll`.
`README.md` example at line ~110. `requirements/protocol-requirements.md` § Infinite scroll.

---

## Phase 5: Big integer support (P16)

**Goal:** opt-in transport of integers outside ±(2⁵³−1) as `{"$bigint":"<digits>"}` for **props and flash**,
plus the page-level flag `"preserveBigIntegers": true`. Without that flag the client does not revive markers
(verified in client `json.ts`: `if (page?.preserveBigIntegers === true && text.includes('"$bigint"'))`).
Client requirement: `@inertiajs/*` **≥ 3.8.0**.

### Steps

1. `InertiaOptions.PreserveBigIntegers` (`bool`, default `false`), with an XML doc mentioning the client version.
2. `InertiaResult.WithPreserveBigIntegers(bool preserve = true)` stores a `bool?` override.
   Effective = `override ?? options.PreserveBigIntegers`.
3. `PageModel`: `[JsonIgnore(WhenWritingNull)] public bool? PreserveBigIntegers { get; set; }`, set to `true` only
   when effective.
4. New `src/Ponango.Inertia/BigIntegerJsonConverters.cs` (internal):
   - `const long MaxSafeInteger = 9007199254740991;`
   - A `JsonConverterFactory` that handles `long`, `ulong`, `Int128`, `UInt128`, `System.Numerics.BigInteger`.
     Write `{ "$bigint": value.ToString(CultureInfo.InvariantCulture) }` when outside the safe range, otherwise a
     number. (`int`, `uint`, `short` etc. are always safe, so leave them alone.) Nullable versions are handled
     automatically by System.Text.Json once the underlying converter exists. Write-only: `Read` can throw
     `NotSupportedException`.
   - A `JsonElement` converter. Flash values come back from `InertiaFlash.ReadAll` as `JsonElement`, so big
     numbers hidden inside them must be rewritten too. Recursively write the element, and for
     `JsonValueKind.Number` where `TryGetInt64` (or `BigInteger.TryParse` of the raw text) shows an integer outside
     the safe range, write the marker. Otherwise `element.WriteTo(writer)`.
   - Do **not** touch `decimal`/`double` (not integers; Laravel only handles `int`).
5. `PageModel.ToJson(IJsonSerializerOptionBuilder, bool preserveBigIntegers = false)`: after
   `serializerOptions.SetSerializerOptions(options)`, if `preserveBigIntegers` then
   `options.Converters.Insert(0, new BigIntegerConverterFactory()); options.Converters.Insert(0, new BigIntegerJsonElementConverter());`.
   Insert at index 0 so ours wins over user converters (document this precedence). The page envelope has no big
   integers, so converting the whole page is equivalent to converting only props + flash. Use
   `PageModel.PreserveBigIntegers == true` as the switch, so both the JSON path and `HtmlHelperExtensions.InertiaRender`
   (which calls `data.ToJson(serializer)`) pick it up automatically. Make `ToJson` read `this.PreserveBigIntegers`
   rather than taking a parameter, which keeps `InertiaRender`'s signature unchanged.
6. (Perf note, optional within this phase) `ToJson` builds a new `JsonSerializerOptions` on every call, which defeats
   System.Text.Json's metadata cache. Consider caching two instances (with/without big-int converters) in a static
   `ConditionalWeakTable<IJsonSerializerOptionBuilder, ...>`. Only do this if it stays simple. It is not required.

### Tests: new file `tests/Ponango.Inertia.Tests/BigIntegerTests.cs`

1. `Disabled_by_default_emits_plain_numbers_and_no_flag`.
2. `Global_option_wraps_unsafe_long_and_sets_flag`: `900719925474099988L` → `{"$bigint":"900719925474099988"}`,
   `preserveBigIntegers == true`.
3. `Safe_range_values_stay_numbers`: `9007199254740991L`, `-9007199254740991L`, `42L`.
4. `Negative_unsafe_long_is_wrapped`.
5. `Ulong_Int128_and_BigInteger_are_wrapped`.
6. `Nested_values_in_anonymous_objects_arrays_and_dictionaries_are_wrapped`.
7. `Nullable_long_is_wrapped_and_null_stays_null`.
8. `Per_response_opt_in_and_opt_out_override_global`.
9. `Flash_big_integer_is_wrapped` (round trip through `InertiaFlash` → `JsonElement` path).
10. `Html_initial_payload_wraps_big_integers_and_sets_flag` (via `InertiaRender`).
11. `Markers_are_not_emitted_for_decimal_or_double`.

### Docs

New section "Big integers" in `docs/advanced-topics.md` (enable globally/per response, the wire format, the client
≥ 3.8.0 requirement, and that submitted BigInts arrive as numeric strings that bind fine to `long`). Add a line to the
`README.md` Features list. `requirements/protocol-requirements.md` core response model: add `preserveBigIntegers`.
`requirements/public-api-requirements.md`: `WithPreserveBigIntegers`, `InertiaOptions.PreserveBigIntegers`.

---

## Phase 6: Small options and conveniences

### 6.1 All errors per field

- `InertiaOptions.WithAllErrors` (`bool`, default `false`).
- `InertiaResult.WithErrors`: when enabled, emit `string[]` (all `ErrorMessage`s) per key instead of the first one.
  Error-bag nesting is unchanged.
- Tests: default single string (existing test still green); option on → arrays; with bag → arrays under the bag.
- Docs: `docs/advanced-topics.md` § Validation errors and error bags.

### 6.2 Option to hide `sharedProps` metadata

- `InertiaOptions.ExposeSharedPropKeys` (`bool`, default `true`). When false, `PageModel.SharedProps` stays null.
  Values are still emitted.
- Test both settings. Docs: mention that instant visits need it on.

### 6.3 Lazy delegate props

The partial-reloads docs recommend "lazy data evaluation" (closures only run when the prop is included).
Today a raw `Func<object>` prop value isn't invoked at all; it's handed to the serializer.

- In the resolver, treat values of type `Func<object?>`, `Func<Task<object?>>`, `Func<object>` and
  `Func<Task<object>>` as resolvable regular props: invoke only if the prop survives filtering.
- Tests: a partial reload that excludes it never calls the delegate; a full visit calls it once; async delegate.
- Docs: § Partial reloads, "Lazy evaluation" example:
  `["companies"] = (Func<object>)(() => db.Companies.ToList())`.

### 6.4 `WithFlash(IDictionary<string, object?>)` overload

If you did not already add it in Phase 2, add it here with a test.

---

## Phase 7 (optional, can be deferred to 3.1): nested prop types and dot-notation partial reloads

**Goal:** v3 "Nested Prop Types". Wrappers work inside nested objects, and `only`/`except`/`reset`/metadata use
dot paths (`auth.notifications`). Only start this if the maintainer approves the scope. It is the riskiest phase
because it touches serialization shape.

Design constraints for .NET:

- Recurse only into containers that can be **reshaped without changing their JSON**:
  `IDictionary<string, object?>` / `IDictionary<string, object>` and C# **anonymous types**
  (`[CompilerGenerated]` + name contains `AnonymousType`). Do **not** walk arbitrary POCOs; they serialize as is.
- Convert a container to `Dictionary<string, object?>` **only if its subtree contains a prop wrapper or lazy
  delegate** (scan first). Untouched subtrees keep their original objects, so their serialization is unchanged.
- When converting an anonymous type, apply `JsonSerializerOptions.PropertyNamingPolicy` to member names (dictionary
  keys are not renamed by the serializer, so you must do it by hand to keep the output identical). When converting
  a dictionary, apply `DictionaryKeyPolicy` if set. Get the effective options from `IJsonSerializerOptionBuilder`.
- Top-level dot keys (`["auth.user"] = ...`) are unpacked into nested dictionaries (`unpackDotProps`). The shared
  prop key recorded is the first segment.
- Port the recursion from `PropsResolver::resolveProps`: the `parentWasResolved` flag (children of a value produced
  by a wrapper/delegate bypass partial filtering), `MatchesOnly`/`LeadsToOnly`/`MatchesExcept` prefix matching, and
  metadata paths using the dot path.

Tests (new `NestedPropsTests.cs`): nested deferred announced as `auth.notifications`; `only: auth.notifications`
resolves only that leaf plus ancestors; `except: auth.invoices`; nested once/merge metadata paths; anonymous-type
camelCase preservation; untouched POCO subtree serializes identically (compare JSON before/after the feature for a
prop tree without wrappers).

Docs: § Partial reloads, "Nested props and dot notation".

---

## Phase 8: Documentation, changelog, final verification

1. `CHANGELOG.md` `## 3.0.0 (unreleased)`, with sections `### Added`, `### Changed`, `### Fixed`,
   `### Breaking changes`, collecting every phase's entries. Breaking list at minimum: flash location; except-only
   partial semantics; fragment redirect handling; `errors` always present; `Inertia.Defer` signature (binary);
   merge-intent header restricted to scroll props; `MergeProp.WithScroll` obsolete.
2. New doc `docs/upgrading-to-3.0.md` (link it from `README.md` § Upgrade Notes and `docs/getting-started.md`
   § Next steps): one section per breaking change, with before/after JSON and the client-side change (e.g.
   `usePage().props.flash` → `usePage().flash`). Also: the client must be `@inertiajs/*` 3.x, and ≥ 3.8.0 for big
   integers.
3. `README.md`: Features list (combined props, rescue, scroll, big integers), Example APIs, Configuration
   (`PreserveBigIntegers`, `WithAllErrors`, `ExposeSharedPropKeys`), How It Works (flash, errors), plus an
   "Unsupported v3 features" list from §1.5.
4. `AGENTS.md`: capability list, Fluent response APIs (`WithPreserveBigIntegers`, `WithFlash(dict)`), Static factory
   helpers (`Scroll`, `DeepMerge`, `Defer(..., rescue)`), Prop Wrapper Semantics, Request/Protocol Notes.
   **Fix the build/test commands** to use `-p:` style (works in Git Bash and PowerShell).
5. `requirements/*.md`: confirm every section touched in Phases 1–7 is consistent. In particular
   `protocol-requirements.md` core response model should list `flash`, `rescuedProps`, `preserveBigIntegers`.
6. Package description in `Ponango.Inertia.csproj` `<Description>`: mention rescued/combined props and big integers
   (description only; **do not** change version properties).
7. Final verification:
   - Full test suite green. Record the final count in the PR description; expect roughly 35 + 60 new tests.
   - `dotnet pack src/Ponango.Inertia/Ponango.Inertia.csproj -c Release` succeeds with no new warnings in library code.
   - Grep sanity:
     `grep -rn "MergeFlashIntoSharedProps\|ToUnixTimeSeconds\|GetEffectiveMergeMode" src/` returns nothing.
   - Optional manual smoke test, if a sample app is available: a Vue/React app on `@inertiajs/*@^3.8` that checks
     `page.flash`, a deferred `rescue` slot, an infinite scroll reset, and BigInt display.

---

## Appendix A: Wire-format cheat sheet (target state)

Full visit with everything enabled:

```json
{
  "component": "Users/Index",
  "props": { "errors": {}, "user": { "id": { "$bigint": "900719925474099988" } }, "posts": { "data": [ ... ] } },
  "url": "/users",
  "version": "6b16b94d7c51cbe5b1fa42aac98241d5",
  "sharedProps": ["auth"],
  "mergeProps": ["posts.data"],
  "matchPropsOn": ["posts.data.id"],
  "scrollProps": { "posts": { "pageName": "page", "previousPage": null, "nextPage": 2, "currentPage": 1, "reset": false } },
  "deferredProps": { "default": ["permissions"] },
  "onceProps": { "roles": { "prop": "memberRoles", "expiresAt": 1791292130000 } },
  "flash": { "message": "Saved" },
  "preserveBigIntegers": true
}
```

Partial reload where a rescued deferred prop failed:

```json
{ "component": "Users/Index", "props": { "errors": {} }, "url": "/users", "version": "…", "rescuedProps": ["permissions"] }
```

Control responses (no body, no `X-Inertia` header):

| Situation | Status | Headers |
|---|---|---|
| Asset version mismatch (GET) | 409 | `X-Inertia-Location: <full request URL>`, `X-Inertia-Version: <current>` |
| External redirect (any, fragment or not) | 409 | `X-Inertia-Location: <target>` |
| Internal redirect with `#fragment`, non-prefetch | 409 | `X-Inertia-Redirect: <target>` |
| `Inertia.Location(url)` | 409 | `X-Inertia-Location: <absolute url>` |
| 302 after non-GET (no fragment, internal) | 303 | `Location` |

## Appendix B: Existing tests that change on purpose

| Test | Phase | Change |
|---|---|---|
| `InertiaResultTests.Partial_except_does_not_resolve_optional_or_deferred_props` | 1 | Invert: except-only partial resolves non-excluded optional/deferred |
| `InertiaMiddlewareTests.External_redirect_with_fragment_uses_redirect_header` | 1 | External + fragment → `X-Inertia-Location` |
| `PlanCoverageTests.Inertia_render_emits_v3_script_payload_and_escapes_script_end_tags` | 1 | Assert `/` → `\/` and `<` → `<`; parse body as JSON |
| `PlanCoverageTests.Middleware_returns_conflict_when_asset_version_mismatches` | 1 | Add `X-Inertia-Version` assertion (extension, not inversion) |
| `InertiaResultTests.Flash_is_merged_with_existing_shared_flash_values` | 2 | Flash at `root.flash`; shared `flash` prop independent |
| `PlanCoverageTests.Flash_values_are_emitted_once_and_merge_with_object_shaped_flash` | 2 | Flash at `root.flash`, absent on 2nd response |
| `ApiErgonomicsTests.Render_uses_top_level_props_and_fluent_with_helpers` | 2 | `root.flash.success` |
| `PlanCoverageTests.Once_props_emit_metadata_and_respect_except_header` | 3 | Second response keeps `onceProps.plans` |

Any other existing test that fails is a regression.
