# Plan: Carry validation errors across redirects

- **Status:** not started (deferred)
- **Written:** 2026-10-06, against branch `feat/inertia-v3-alignment` (PR #5, the 3.0.0 work)
- **Target release:** 3.0.0 if it lands before that release, otherwise 3.1.0
- **Size:** small (about half a day including tests and docs)
- **Executor:** an AI coding agent. Read `AGENTS.md` and `README.md` first, and follow the rules in
  `ai/plans/inertia-v3-protocol-alignment.md` §0.1 (test first, CRLF line endings, docs in the same change, one commit).

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

## 3. Current code (what you will touch)

| File | Relevant today |
|---|---|
| `src/Ponango.Inertia/InertiaResult.cs` | `WithErrors(ModelStateDictionary, string? errorBag)` builds the errors dictionary inline (first message, or `string[]` when `InertiaOptions.WithAllErrors`), resolves the bag (explicit arg, then header), and stores it in `Props["errors"]`. `BuildPageModelAsync` adds `errors = {}` when absent and pulls flash with `InertiaContext.PullFlash()`. |
| `src/Ponango.Inertia/InertiaFlash.cs` | TempData-backed store. Keys `__inertia_flash_{key}`, values JSON-serialized, read back as `JsonElement`, removed on read. Use it as the model. |
| `src/Ponango.Inertia/InertiaContext.cs` | `Flash(...)`, `PullFlash()`, `Headers.ErrorBag`, `IsPrefetch`, `IsInertia`. |
| `src/Ponango.Inertia/ServiceCollectionExtensions.cs` | `AddInertia(...)` registers options, `InertiaFlash`, `InertiaContext`, and the serializer builder. No MVC filters are registered yet. |
| `src/Ponango.Inertia/InertiaRedirectResult.cs` | `InertiaController.Redirect(...)` result (a `RedirectResult`). |
| `src/Ponango.Inertia/InertiaOptions.cs` | `WithAllErrors`, etc. |
| `tests/Ponango.Inertia.Tests/TestInfrastructure.cs` | The TempData provider stores in `HttpContext.Items`, so "the next request" is simulated by `TestHelpers.FreshInertia(test)` on the same `HttpContext` (see `FlashTests.Flash_survives_redirect_until_a_page_is_rendered`). |

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

Refactor `WithErrors` to use it. Existing tests must pass unchanged.

### 4.2 Store: `InertiaValidationErrors` (new, TempData-backed, scoped service)

Model it on `InertiaFlash`:

- `void Store(object scopedErrors)`: JSON-serialize to TempData key `__inertia_errors` and call `Save()`.
- `object? Pull()`: read, deserialize to `JsonElement`, remove, `Save()`, and return it (`null` when absent).
- Register it in `AddInertia` (`services.AddScoped<InertiaValidationErrors>()`) and expose it to `InertiaContext`
  through an optional constructor parameter, as `InertiaFlash` is passed today. Keep the existing public
  constructors source-compatible: add the new parameter as optional and last.

The bag is resolved **when the errors are stored**: an explicit bag argument, else the POST request's
`X-Inertia-Error-Bag`. On read, if the stored errors are flat and the GET request carries `X-Inertia-Error-Bag`,
nest them under that bag. That matches Laravel, where the header on the follow-up request decides.

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
5. not a precognition request (those short-circuit with 204/422 before any redirect, but guard anyway)
6. `InertiaOptions.PersistValidationErrorsOnRedirect` is true

Register it from `AddInertia`:
`services.Configure<MvcOptions>(o => o.Filters.Add<InertiaValidationErrorsFilter>());` The filter must be
resolvable from DI: register it with `services.AddScoped<InertiaValidationErrorsFilter>()`, or add it as a
`ServiceFilterAttribute`-style type filter.

### 4.4 Explicit capture (for code that doesn't use ModelState, or when you don't want automatic capture)

- `InertiaContext.FlashErrors(ModelStateDictionary modelState, string? errorBag = null)`
- `InertiaContext.FlashErrors(IDictionary<string, string> errors, string? errorBag = null)` (custom validation,
  FluentValidation results converted by the app)

Both store through `InertiaValidationErrors`, as above.

### 4.5 Read side (`InertiaResult.BuildPageModelAsync`)

- If the response set `errors` explicitly (`WithErrors` or `Props["errors"]`), that value wins. Still
  `Pull()` the stored errors so they don't leak into a later page.
- Otherwise use the pulled errors, or `{}` when there are none (today's default).
- Pull only when a page is actually built (a version-mismatch 409 leaves them in place, as with flash).
- `errors` stays an always-included prop, so partial reloads keep working unchanged.

### 4.6 Optional: `Back()` helper

ASP.NET has no `redirect()->back()`. Add `InertiaContext.Back(string fallbackUrl = "/")` (and
`InertiaController.Back(...)`), which redirects to the `Referer` header when it is a local URL, else to
`fallbackUrl`. It returns an `InertiaRedirectResult`, so the middleware's `303` conversion and the error filter
both apply. That makes the common flow one line:

```csharp
if (!ModelState.IsValid)
    return Back();
```

Only accept local referers (`IUrlHelper.IsLocalUrl`, or same host). Don't open-redirect to arbitrary referers.

### 4.7 Options

```csharp
/// When true (default), validation errors from an Inertia non-GET request that ends in a redirect are kept for
/// the next rendered page, like Laravel's redirect-back-with-errors.
public bool PersistValidationErrorsOnRedirect { get; set; } = true;
```

## 5. Decisions to confirm before starting

1. **On by default?** Recommended yes: it is the behavior Inertia apps expect, and it only affects Inertia
   non-GET requests that redirect with an invalid `ModelState`. It is a behavior change, so it needs a changelog
   entry: under `### Changed` in 3.0.0 if 3.0.0 is still unreleased, else a 3.1.0 entry that names the option for
   opting out.
2. **Prefetch requests.** A prefetch GET that lands between the redirect and the real visit would consume the
   stored errors (and flash) and cache a page containing them. Recommended: don't pull errors or flash on prefetch
   requests (`InertiaContext.IsPrefetch`). This also changes flash behavior, so add a flash test. Check the current
   inertia-laravel behavior first: search `src/Response.php` and `src/Middleware.php` for `prefetch`, and match it
   if it differs.
3. **Include `Back()`?** Recommended yes. It's small and makes the documented flow natural.

## 6. Tests (new file `tests/Ponango.Inertia.Tests/ValidationErrorRedirectTests.cs`)

Simulate "next request" the same way `FlashTests` does (same `HttpContext` + `TestHelpers.FreshInertia(test)`,
resetting the method and headers between requests). For the filter, build a `ResultExecutingContext` around an
`ActionContext` with a populated `ModelState`, as `PrecognitionTests` does for `ActionExecutingContext`.

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
11. `FlashErrors_explicit_api_stores_errors` (both overloads).
12. `Option_off_disables_automatic_capture`.
13. `Version_mismatch_409_keeps_stored_errors`.
14. `Prefetch_request_does_not_consume_errors_or_flash` (if decision 2 is accepted).
15. `Back_redirects_to_local_referer_else_fallback` (rejects an external referer), and `Back` from a PUT becomes
    303 through the middleware.
16. Regression: everything in `PlanCoverageTests.With_errors_supports_flat_explicit_bag_and_header_bag_shapes`,
    `OptionsAndConveniencesTests` and `FlashTests` passes unchanged (except the prefetch flash change, if accepted).

## 7. Docs to update in the same change

- `docs/advanced-topics.md` § Validation errors and error bags: show the redirect flow, `Back()`, `FlashErrors`,
  and the opt-out option.
- `docs/getting-started.md` § 7 "Return validation errors": mention that redirecting back works too.
- `docs/compatibility.md`: change "Validation and error bags" from ⚠️ to ✅, and "Empty response / redirect back"
  if `Back()` lands (`Back()` is not the same as an empty-response redirect, so keep that row ❌ and add a note).
- `README.md`: remove the validation-errors sentence from "Not Supported Yet"; add the option to the Configuration
  example.
- `requirements/protocol-requirements.md` § Error bags, `requirements/public-api-requirements.md` (new APIs and
  option), `AGENTS.md` (fluent/context APIs, Prop/Request notes), `CHANGELOG.md`.

## 8. Exit criteria

- All new tests pass, and the full suite passes.
- `dotnet build` of the library shows 0 warnings.
- The docs above are updated; the compatibility table no longer lists the gap.
