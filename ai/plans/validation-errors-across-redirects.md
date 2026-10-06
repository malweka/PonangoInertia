# Plan: Carry validation errors across redirects

- **Status:** not started (deferred)
- **Written:** 2026-10-06, against branch `feat/inertia-v3-alignment` (PR #5, the 3.0.0 work)
- **Re-checked:** 2026-10-06, against `main` at `9bf8ec0` (PR #5 merged, including the review follow-up
  `8aa262e`). `CHANGELOG.md` still has `## 3.0.0 (unreleased)`. Baseline: 133 tests pass, 0 fail.
- **Target release:** 3.0.0 if it lands before that release, otherwise 3.1.0
- **Size:** small (about half a day including tests and docs)
- **Executor:** an AI coding agent. Read `AGENTS.md` and `README.md` first, and follow the rules in
  `ai/plans/inertia-v3-protocol-alignment.md` §0.1 (test first, CRLF line endings, docs in the same change). This
  plan is a single change: work on a new branch from `main` (for example `feat/validation-errors-across-redirects`),
  not the old `feat/inertia-v3-alignment` branch that §0.1 names, and make one commit.

---

## 1. Problem

The usual Inertia form flow is: POST → validation fails → redirect back → the GET that follows renders the form
page with `props.errors` filled in. Ponango.Inertia only returns errors when the action re-renders the page
itself:

```csharp
return _inertia.Render("Users/Create", new { form }).WithErrors(ModelState);   // works today
```

If the action redirects instead, the errors are lost and the form page shows `errors: {}`:

```csharp
if (!ModelState.IsValid)
    return RedirectToAction(nameof(Create));                                   // errors lost today
```

`docs/compatibility.md` lists this as ⚠️ under "Validation and error bags". The README's "Not Supported Yet"
section mentions it too.

## 2. How the reference adapter does it

From `inertia-laravel` 3.x `src/Middleware.php`:

- `$request->validate()` throws, and Laravel redirects back with the errors in the session (`errors`, a
  `ViewErrorBag` holding named bags).
- On the next request, the middleware shares
  `'errors' => Inertia::always($this->resolveValidationErrors($request))`.
- `resolveValidationErrors` returns `{}` when the session has no errors. Otherwise it maps each bag to
  `field => first message` (or all messages when `$withAllErrors` is set). When the request carries
  `X-Inertia-Error-Bag` and only the `default` bag exists, the result is nested under that bag name.
- The session flashes the errors for exactly one request. On redirects the middleware calls `reflash()` for
  Inertia flash data, and on a version-mismatch 409 it calls `$session->reflash()`, so pending data survives.

The protocol notes that browsers replay request headers when they follow a redirect, so `X-Inertia-Error-Bag`
reaches the follow-up GET.

ASP.NET Core TempData needs no reflash: a key stays until it is read, and `InertiaFlash` only reads (and removes)
its keys when a page is built.

## 3. Current code (what you will touch)

| File | Relevant today |
|---|---|
| `src/Ponango.Inertia/InertiaResult.cs` | `WithErrors(ModelStateDictionary, string? errorBag)` (line ~128) builds the errors dictionary inline (first message, or `string[]` when `InertiaOptions.WithAllErrors`, read from `InertiaContext.Request.HttpContext.RequestServices`), resolves the bag (explicit arg, then `Headers.ErrorBag`), and stores it in `Props["errors"]`. `BuildPageModelAsync` merges shared props first (skipping keys the page overrides), then page props, and adds `errors = {}` only when **neither** has an `errors` key. It pulls flash with `InertiaContext.PullFlash()` at the end. `EnsureProtocolFallback` returns a 409 before any page is built. |
| `src/Ponango.Inertia/InertiaFlash.cs` | Public, TempData-backed store. Keys `__inertia_flash_{key}`, values serialized with the default `JsonSerializer` options, read back with `Deserialize<object>` (a `JsonElement`), removed on read, then `Save()`. Use it as the model. |
| `src/Ponango.Inertia/InertiaContext.cs` | Two public constructors, `(IHttpContextAccessor, IAssetVersionProvider, InertiaFlash? flash = null)` and `(HttpContext, IAssetVersionProvider, InertiaFlash? flash = null)`. Members: `Flash(...)`, internal `PullFlash()`, `Headers.ErrorBag`, `IsPrefetch`, `IsInertia`, `Request`. |
| `src/Ponango.Inertia/InertiaExtensions.cs` | `Render(...)` and `Location(...)` are **extension methods** on `InertiaContext` here, with `GetPageUrl` and the private `InertiaLocationResult`. `Back(...)` belongs next to `Location`. |
| `src/Ponango.Inertia/InertiaMiddleware.cs` | The version-mismatch 409 returns **before** `_next`, so no page is built and nothing is pulled. After `_next`, external redirects become 409 + `X-Inertia-Location`, internal `#fragment` redirects become 409 + `X-Inertia-Redirect` (not for prefetch), and 302 becomes 303 for non-GET Inertia requests. `IsExternalUrl(location, request)` is `private static`. |
| `src/Ponango.Inertia/InertiaRedirectResult.cs` | `internal` class deriving from `RedirectResult`, returned by `InertiaController.Redirect(...)`. For non-GET requests it writes the `303` and `Location` itself (it does not rely on the middleware). |
| `src/Ponango.Inertia/InertiaController.cs` | `Redirect(...)` override, `Render(...)`, `Location(...)`, `IsInertia`/`IsPrefetch`/`IsPrecognition`. |
| `src/Ponango.Inertia/ServiceCollectionExtensions.cs` | `AddInertia(...)` registers options (`Options.Create`), `AddHttpContextAccessor`, scoped `InertiaFlash` and `InertiaContext`, and the serializer builder. No MVC filters or `MvcOptions` configuration yet. |
| `src/Ponango.Inertia/InertiaOptions.cs` | `RootView`, `EncryptHistory`, `PreserveBigIntegers`, `WithAllErrors`, `ExposeSharedPropKeys`, `SharedData`, `JsonSerializerOptions`. |
| `src/Ponango.Inertia/PropsResolver.cs` | Always includes `errors` on partial reloads (`path == "errors"`, line ~204). No change needed. |
| `src/Ponango.Inertia/PrecognitiveAttribute.cs` | Has its own `ExtractErrors` (first message only) for the 422 body. Out of scope: leave it as it is. |
| `tests/Ponango.Inertia.Tests/TestInfrastructure.cs` | The TempData provider stores in `HttpContext.Items`. It calls `AddMvcCore()` and registers `IUrlHelperFactory`. |
| `tests/Ponango.Inertia.Tests/TestHelpers.cs` | `FreshInertia(test)` builds `new InertiaContext(httpContext, versionProvider, flash)` on the same `HttpContext` to simulate the next request (see `FlashTests.Flash_survives_redirect_until_a_page_is_rendered`). **It must also pass the new errors store**, or the read side will never see stored errors. |

## 4. Design

### 4.1 One place that turns ModelState into the errors shape

Extract the dictionary-building code from `InertiaResult.WithErrors` into an internal helper, so the render path
and the redirect path produce identical JSON:

```csharp
internal static class ValidationErrors
{
    // field -> first message (string), or all messages (string[]) when allErrors is true.
    public static Dictionary<string, object> FromModelState(ModelStateDictionary modelState, bool allErrors);

    // Nests under the bag name when one is given.
    public static object Scope(Dictionary<string, object> errors, string? bag);
}
```

Refactor `WithErrors` to use it. Existing tests must pass unchanged. Don't touch `PrecognitiveAttribute`.

### 4.2 Store: `InertiaValidationErrors` (new, TempData-backed, scoped service)

Model it on `InertiaFlash`:

- `void Store(Dictionary<string, object> errors, string? bag)`: JSON-serialize `{ bag, errors }` (the flat errors
  and the bag name, kept separate) to the TempData key `__inertia_errors` and call `Save()`. Use the default
  `JsonSerializer` options, as `InertiaFlash` does.
- `object? Pull(string? requestBag)`: read, remove, `Save()`. Rebuild the flat errors as
  `Dictionary<string, object>` (values `string` or `string[]`), not a raw `JsonElement`, so they serialize through
  the same path as `WithErrors` (a configured `DictionaryKeyPolicy` applies the same way). Then return
  `ValidationErrors.Scope(errors, storedBag ?? requestBag)`, or `null` when nothing is stored.
- Register it in `AddInertia` (`services.AddScoped<InertiaValidationErrors>()`) and pass it to `InertiaContext`
  through a new optional constructor parameter, `InertiaValidationErrors? validationErrors = null`, added last on
  **both** constructors, as `InertiaFlash` is passed today. That keeps them source-compatible. It changes the
  constructor signatures, which is fine in 3.0.0 (a major release). If this ships in 3.1.0 instead, add new
  constructor overloads and keep the old ones.
- Add `internal object? PullErrors()` on `InertiaContext` beside `PullFlash()`, passing `Headers.ErrorBag`.

The bag is resolved **when the errors are stored**: an explicit bag argument, else the POST request's
`X-Inertia-Error-Bag`. If neither gives one, the follow-up GET's `X-Inertia-Error-Bag` decides when the errors are
read, which matches Laravel, where the header on the follow-up request decides.

### 4.3 Automatic capture: `InertiaValidationErrorsFilter` (new MVC result filter)

```csharp
internal sealed class InertiaValidationErrorsFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // Inertia request, non-GET, invalid ModelState, and the action is redirecting.
        // Every MVC redirect result implements IKeepTempDataResult (RedirectResult, RedirectToAction/Route/Page,
        // LocalRedirectResult), which also covers InertiaRedirectResult.
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
```

Conditions for storing:

1. `InertiaContext.IsInertia` (plain MVC and API requests are untouched)
2. the request method is not GET
3. `!context.ModelState.IsValid`
4. `context.Result is IKeepTempDataResult` (a redirect)
5. not a precognition request (`[Precognitive]` short-circuits with a 204/422 result before the action, so this
   is a guard only)
6. `InertiaOptions.PersistValidationErrorsOnRedirect` is true

Register it from `AddInertia` with `services.Configure<MvcOptions>(o => o.Filters.Add<InertiaValidationErrorsFilter>());`.
`Filters.Add<T>()` adds a type filter that is created with `ActivatorUtilities`, so its constructor dependencies
come from DI and the filter itself does not need to be registered (only `Filters.AddService<T>()` needs that).
Global `MvcOptions` filters apply to controllers and Razor Pages. Minimal API endpoints don't run MVC filters: they
use `FlashErrors` (§4.4).

### 4.4 Explicit capture (for code that doesn't use ModelState, or when you don't want automatic capture)

- `InertiaContext.FlashErrors(ModelStateDictionary modelState, string? errorBag = null)`
- `InertiaContext.FlashErrors(IDictionary<string, string> errors, string? errorBag = null)` (custom validation,
  FluentValidation results converted by the app)

Both store through `InertiaValidationErrors`, as above. The `ModelStateDictionary` overload honors
`WithAllErrors`; read the options from `Request.HttpContext.RequestServices`, as `WithErrors` does.

### 4.5 Read side (`InertiaResult.BuildPageModelAsync`)

Today the `errors = {}` default is added when the merged list (shared props, then page props) has no `errors` key.
Keep that order and put the stored errors in the same place:

1. Always call `InertiaContext.PullErrors()` when a page is built, so stored errors never leak into a later page.
2. If the merged list already has `errors` (page `WithErrors` / `Props["errors"]`, or a shared `errors` the app
   set itself), that value wins and the pulled errors are dropped.
3. Otherwise add the pulled errors, or `{}` when there are none (today's default).

Pull only when a page is actually built: the middleware's version-mismatch 409 and `EnsureProtocolFallback` both
return before `BuildPageModelAsync`, so stored errors stay for the follow-up request, as flash does. `errors` stays
an always-included prop, so partial reloads keep working unchanged.

### 4.6 Optional: `Back()` helper

ASP.NET has no `redirect()->back()`. Add `Back(this InertiaContext context, string fallbackUrl = "/")` to
`InertiaExtensions` next to `Location`, and `InertiaController.Back(string fallbackUrl = "/")`. It returns a
`RedirectResult` (an `InertiaRedirectResult` underneath, which is internal), so the 303 for non-GET requests and
the error filter both apply.

```csharp
if (!ModelState.IsValid)
    return Back();
```

The `Referer` header is normally an **absolute** URL, and `IUrlHelper.IsLocalUrl` rejects every absolute URL, so
don't use it on the raw header. Parse the referer as an absolute URI, accept it only when it has the request's
host and port (make `InertiaMiddleware.IsExternalUrl` `internal` and reuse it), and redirect to its path and query.
Otherwise accept a relative referer only when `IsLocalUrl` passes, else use `fallbackUrl`. Never redirect to
another host.

### 4.7 Options

```csharp
/// When true (default), validation errors from an Inertia non-GET request that ends in a redirect are kept for
/// the next rendered page, like Laravel's redirect-back-with-errors.
public bool PersistValidationErrorsOnRedirect { get; set; } = true;
```

## 5. Decisions to confirm before starting

1. **On by default?** Recommended yes: it is the behavior Inertia apps expect, and it only affects Inertia
   non-GET requests that redirect with an invalid `ModelState`. It is a behavior change, so it needs a changelog
   entry: under `### Changed` in 3.0.0 while 3.0.0 is still unreleased (it is, as of the re-check), else a 3.1.0
   entry that names the option for opting out.
2. **Prefetch requests.** A prefetch GET that lands between the redirect and the real visit would consume the
   stored errors (and flash) and cache a page containing them. Today `BuildPageModelAsync` pulls flash on prefetch
   requests too. Recommended: don't pull errors or flash on prefetch requests (`InertiaContext.IsPrefetch`). This
   also changes flash behavior, so add a flash test. Check the current inertia-laravel behavior first: search
   `src/Response.php` and `src/Middleware.php` for `prefetch`, and match it if it differs.
3. **Include `Back()`?** Recommended yes. It's small and makes the documented flow natural.

## 6. Tests (new file `tests/Ponango.Inertia.Tests/ValidationErrorRedirectTests.cs`)

First update `TestHelpers.FreshInertia` to pass `test.GetRequiredService<InertiaValidationErrors>()`.

Simulate "next request" the same way `FlashTests` does (same `HttpContext` + `TestHelpers.FreshInertia(test)`,
`TestHelpers.ResetResponse` and resetting the method and headers between requests). For the filter, build a
`ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object())` around
`TestInfrastructure.CreateActionContext(...)` with a populated `ModelState`, as `PrecognitionTests` does for
`ActionExecutingContext`, and create the filter with `ActivatorUtilities.CreateInstance` from `test.Services`.

1. `Invalid_model_state_on_inertia_post_redirect_is_shown_on_next_page`: POST + `RedirectToActionResult` + invalid
   state → filter stores → next GET render has `props.errors.email == "Email is required"`.
2. `Errors_are_consumed_once`: the second GET has `errors == {}`.
3. `Non_inertia_post_redirect_does_not_store_errors`.
4. `Get_redirect_does_not_store_errors`.
5. `Valid_model_state_does_not_store_errors`.
6. `Non_redirect_result_does_not_store_errors` (e.g. `Render(...)` or `ContentResult`).
7. `Error_bag_header_on_post_scopes_stored_errors`.
8. `Error_bag_header_on_follow_up_get_scopes_flat_errors`.
9. `WithAllErrors_option_stores_arrays`.
10. `Explicit_WithErrors_on_the_rendered_page_wins_and_stored_errors_are_cleared`.
11. `Shared_errors_prop_wins_over_stored_errors` (an app-level `Share("errors", ...)`), and the stored errors are
    still cleared.
12. `FlashErrors_explicit_api_stores_errors` (both overloads).
13. `Option_off_disables_automatic_capture`.
14. `Version_mismatch_409_keeps_stored_errors` (through the middleware, and through `EnsureProtocolFallback`
    without it).
15. `Stored_errors_survive_partial_reload_filtering`: a partial reload with `only` that doesn't name `errors` still
    gets them.
16. `Prefetch_request_does_not_consume_errors_or_flash` (if decision 2 is accepted).
17. `Back_redirects_to_same_host_referer_else_fallback`: an absolute same-host referer redirects to its path and
    query; an external referer and a missing referer use the fallback. `Back` from a PUT returns 303 (from
    `InertiaRedirectResult` itself, no middleware needed).
18. `Filter_is_registered_by_AddInertia`: `IOptions<MvcOptions>` from the test container lists the filter.
19. Regression: everything in `PlanCoverageTests.With_errors_supports_flat_explicit_bag_and_header_bag_shapes`,
    `OptionsAndConveniencesTests` and `FlashTests` passes unchanged (except the prefetch flash change, if accepted).

## 7. Docs to update in the same change

- `docs/advanced-topics.md` § "Validation errors and error bags": show the redirect flow, `Back()`, `FlashErrors`,
  and the opt-out option.
- `docs/getting-started.md` § 7 "Return validation errors": mention that redirecting back works too.
- `docs/compatibility.md` § "Server features": change "Validation and error bags" from ⚠️ to ✅. Keep
  "Empty response → redirect back" ❌ (`Back()` is not the same as an empty-response redirect) and point its note at
  `Back()`. Update the "Store previous URL" note to mention `Back()`.
- `docs/upgrading-to-3.0.md`: if this lands in 3.0.0, note the new default and the opt-out option.
- `README.md`: in "Not Supported Yet", remove the clause saying validation errors are not carried across a redirect
  (keep the CSRF sentence); extend the "Validation" bullet under "What This Adapter Gives You"; add
  `PersistValidationErrorsOnRedirect` to the "Configuration" example.
- `requirements/protocol-requirements.md` § "Error bags"; `requirements/public-api-requirements.md` § "Response
  builder" / "Controller conveniences" / "Configuration" (new APIs and option).
- `AGENTS.md`: "Current Public API" (`Back`, `FlashErrors`), "Core Architecture" (`InertiaResult` reads stored
  errors; the new filter), "Request/Protocol Notes", and the "Tests" list (`ValidationErrorRedirectTests`).
- `CHANGELOG.md` under `## 3.0.0 (unreleased)` (or a new 3.1.0 entry, see decision 1).

## 8. Exit criteria

- All new tests pass, and the full suite passes (133 before this change).
- `dotnet build` of the library shows 0 warnings.
- The docs above are updated; the compatibility table no longer lists the gap.
