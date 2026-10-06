# Advanced Topics

This guide covers the parts of `Ponango.Inertia` you will typically use after basic rendering is working.

## Rendering APIs

Preferred API:

```csharp
return _inertia.Render("Users/Index", new
{
    users,
    filters
});
```

Legacy API:

```csharp
return _inertia.Inertia("Inertia", model, "Users/Index");
```

The legacy `Inertia(...)` methods are still available, but they are marked obsolete in favor of `Render(...)`.

## Shared data

Application-wide shared props can be configured once:

```csharp
builder.Services.AddInertia(options =>
{
    options.SharedData = ctx => new Dictionary<string, object>
    {
        ["auth"] = new
        {
            name = ctx.User.Identity?.Name
        }
    };
});
```

Or shared at request time:

```csharp
_inertia.Share("featureFlags", new
{
    betaSearch = true
});
```

Shared keys are tracked in the page object’s `sharedProps` field.

## Flash messages

`WithFlash(...)` stores one-time data and merges it into the response being
built:

```csharp
return _inertia.Render("Users/Index", new { users })
    .WithFlash("success", "User created");
```

Flash values are exposed under the `flash` prop, backed by ASP.NET Core
TempData, and cleared after they are read.

## Validation errors and error bags

Basic validation:

```csharp
return _inertia.Render("Users/Create", new { form = request })
    .WithErrors(ModelState);
```

Named error bag:

```csharp
return _inertia.Render("Users/Create", new { form = request })
    .WithErrors(ModelState, "createUser");
```

If the client sends `X-Inertia-Error-Bag`, that bag is used automatically unless you explicitly pass one.

## Prop wrappers

Use the `Inertia` static helper to construct prop wrappers.

### Optional props

Only returned when explicitly requested in `X-Inertia-Partial-Data`.

```csharp
result.With("analytics", Inertia.Optional(() => _analytics.GetSummary()));
```

### Always props

Always included, even during partial reloads.

```csharp
result.With("auth", Inertia.Always(() => GetAuthPayload()));
```

### Deferred props

Excluded from the initial full visit and exposed via `deferredProps`.

```csharp
result.With("report", Inertia.Defer(() => _reports.Build(), group: "dashboard"));
```

### Once props

Resolved once and skipped when the client sends `X-Inertia-Except-Once-Props`.

```csharp
result.With("plans", Inertia.Once(() => _billing.GetPlans()));
```

### Merge props

Used for infinite scroll and append/prepend merge semantics.

```csharp
result.With("posts", Inertia.Merge(() => page.Items, MergeMode.Append));
```

## Partial reloads

The adapter supports:

- `X-Inertia-Partial-Data`
- `X-Inertia-Partial-Except`
- `X-Inertia-Reset`

Rules:

- `errors` is always present (an empty object when there are no errors) and always preserved.
- `AlwaysProp` is always preserved.
- When both `X-Inertia-Partial-Data` and `X-Inertia-Partial-Except` are sent, the data list narrows the
  response first and the except list is then removed from it, so a prop named in both is excluded.
- `OptionalProp`, `LazyProp`, and `DeferredProp` are never resolved on a full visit. On a partial reload they
  follow the same `only`/`except` filters as any other prop, so `router.reload({ except: ['users'] })` also
  resolves optional and deferred props that are not excluded.
- Merge metadata is suppressed when the prop key is listed in `X-Inertia-Reset`.

## History and navigation flags

These flags affect the emitted page object:

```csharp
return _inertia.Render("Auth/Login", new { })
    .WithEncryptHistory()
    .WithClearHistory()
    .WithPreserveFragment();
```

Global history encryption default:

```csharp
builder.Services.AddInertia(options =>
{
    options.EncryptHistory = true;
});
```

## Precognition

Apply `[Precognitive]` to a controller or action:

```csharp
[Precognitive]
[HttpPost("/users")]
public IActionResult Store(CreateUserRequest request)
{
    // Normal action logic
}
```

Behavior:

- Valid precognition request: `204` with `Precognition-Success: true`
- Invalid precognition request: `422` with JSON errors
- `Precognition-Validate-Only` limits validation errors to the named fields
  and nested/prefixed keys such as `user.name`
- The action is short-circuited before normal action logic runs
- Every response from a precognitive action carries `Vary: Precognition`

## Prefetch

Use `IsPrefetch` to branch behavior when the client sends `Purpose: prefetch`.

```csharp
if (_inertia.IsPrefetch)
{
    // Skip side effects or expensive logging
}
```

Prefetch requests otherwise use the same response pipeline as normal Inertia requests.

## Infinite scroll

For infinite scroll, combine `MergeProp` with `WithScroll(...)`:

```csharp
return _inertia.Render("Posts/Index", new { })
    .With("posts", Inertia.Merge(() => page.Items, MergeMode.Append)
        .WithScroll(
            currentPage: page.CurrentPage,
            previousPage: page.PreviousPage,
            nextPage: page.NextPage,
            pageName: "page"));
```

This emits:

- `mergeProps` or `prependProps`
- `scrollProps`
- optional `matchPropsOn` if `matchOn` was configured

The client may also send `X-Inertia-Infinite-Scroll-Merge-Intent` with `append` or `prepend` to override append/prepend behavior.

## External redirects

Use `Location(...)` for an external navigation response:

```csharp
return _inertia.Location("https://external-site.com/callback");
```

The response returns `409` with `X-Inertia-Location`.

The middleware also rewrites redirects returned during Inertia requests:

| Redirect target | Response |
|---|---|
| External URL (another host or port), with or without a fragment | `409` + `X-Inertia-Location` (the client does a full `window.location` visit) |
| Internal URL containing a `#fragment` (not a prefetch) | `409` + `X-Inertia-Redirect` (the client makes a fresh Inertia visit and keeps the fragment) |
| Any other `302` after a non-GET request | `303` |

## Asset version mismatches

When an Inertia GET request carries a stale `X-Inertia-Version`, the response is `409` with
`X-Inertia-Location` (the current URL) and `X-Inertia-Version` (the current asset version). Pending flash
data is not consumed by this response, so it is still delivered after the client reloads.
