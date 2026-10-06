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

`WithFlash(...)` stores one-time data that is delivered with the next rendered page, even across a
redirect:

```csharp
return _inertia.Render("Users/Index", new { users })
    .WithFlash("success", "User created");
```

Several values can be flashed at once with `WithFlash(new Dictionary<string, object?> { ... })`, or
from anywhere in the request with `InertiaContext.Flash(key, value)`.

Flash values are emitted in the page object's top-level `flash` field, not in `props`. They are backed by
ASP.NET Core TempData and cleared once a page has been rendered with them. A version-mismatch `409` does not
consume them.

```json
{ "component": "Users/Index", "props": { "errors": {} }, "url": "/users", "version": "…", "flash": { "success": "User created" } }
```

On the client, read them from `usePage().flash` or listen for the `flash` event. Unlike props, flash data is
not stored in browser history, so it does not reappear when the user navigates back:

```js
router.on('flash', (event) => {
  if (event.detail.flash.success) showToast(event.detail.flash.success)
})
```

A prop you share under the name `flash` (for example with `Share("flash", ...)`) is an ordinary prop and is
not merged with flashed values.

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

| Wrapper | Full visit | Partial reload | Metadata |
|---|---|---|---|
| plain value | sent | sent when it passes `only`/`except` | none |
| `Inertia.Always(...)` | sent | always sent, ignores `only`/`except` | none |
| `Inertia.Optional(...)` | not resolved | resolved when it passes `only`/`except` | none |
| `Inertia.Defer(...)` | not resolved, announced | resolved when it passes `only`/`except` | `deferredProps`, `rescuedProps` |
| `Inertia.Merge(...)` | sent | sent when requested | `mergeProps` / `prependProps` / `deepMergeProps`, `matchPropsOn` |
| `Inertia.Once(...)` | sent unless the client already has it | sent when requested (`X-Inertia-Except-Once-Props` ignored) | `onceProps` |

### Optional props

Never resolved on a full visit; only returned when a partial reload selects them.

```csharp
result.With("analytics", Inertia.Optional(() => _analytics.GetSummary()));
```

### Always props

Always included, even during partial reloads.

```csharp
result.With("auth", Inertia.Always(() => GetAuthPayload()));
```

### Deferred props

Excluded from the initial full visit and announced in `deferredProps`, grouped so props in the same group are
fetched in one follow-up request.

```csharp
result.With("report", Inertia.Defer(() => _reports.Build(), group: "dashboard"));
```

#### Rescuing failures

By default an exception thrown while resolving a deferred prop fails the response. With `rescue: true` (or
`.Rescue()`) the exception is logged through `ILogger` (category `Ponango.Inertia`), the prop is omitted (not
sent as `null`), and its key is listed in `rescuedProps`, so the client can render the `rescue` slot of its
`<Deferred>` component:

```csharp
result.With("permissions", Inertia.Defer(() => _permissions.All(), rescue: true));
```

```json
{ "component": "Users/Index", "props": { "errors": {} }, "url": "/users", "version": "…", "rescuedProps": ["permissions"] }
```

### Once props

Resolved once and remembered by the client. On later visits the client sends the key in
`X-Inertia-Except-Once-Props`; the server skips the callback but keeps the `onceProps` entry so the client keeps
the remembered value. A partial reload that requests the prop always resolves it, which is how the client
refreshes it (`router.reload({ only: ['plans'] })`).

```csharp
result.With("plans", Inertia.Once(() => _billing.GetPlans()));

// Expire after a lifetime or at a point in time (expiresAt is sent in Unix milliseconds)
result.With("rates", Inertia.Once(() => _rates.All()).Until(TimeSpan.FromDays(1)));

// Force a fresh value even if the client has one
result.With("plans", Inertia.Once(() => _billing.GetPlans()).Fresh(plansChanged));

// Share one cached value across pages that use different prop names
result.With("memberRoles", Inertia.Once(() => _roles.All()).As("roles"));
```

Share once props for every page with `ShareOnce`:

```csharp
_inertia.ShareOnce("countries", () => _countries.All()).Until(TimeSpan.FromDays(1));
```

### Merge props

The client merges a merge prop with the data it already has on partial reloads, instead of replacing it. Full
visits always replace the value.

```csharp
// Append (default), prepend, or deep merge the whole value
result.With("posts", Inertia.Merge(() => page.Items));
result.With("notifications", Inertia.Merge(() => latest, MergeMode.Prepend));
result.With("chat", Inertia.DeepMerge(() => chatData).MatchingOn("messages.id"));

// Merge only nested arrays and replace the rest of the value
result.With("users", Inertia.Merge(() => usersPage).Append("data", matchOn: "id"));   // mergeProps: ["users.data"]
result.With("forum", Inertia.Merge(() => forum).Append("posts").Prepend("announcements"));
result.With("dashboard", Inertia.Merge(() => dashboard).Append(new Dictionary<string, string?>
{
    ["users.data"] = "id",
    ["messages"] = "uuid",
}));
```

`MatchingOn(...)` (or `matchOn:`) tells the client which field identifies an item, so existing items are updated
in place instead of duplicated. Props listed in `X-Inertia-Reset` are returned without merge metadata, so the
client replaces them.

### Combining prop types

Modifiers can be chained to combine behaviors:

```csharp
result.With("permissions", Inertia.Defer(() => _permissions.All()).Once());       // deferred, then remembered
result.With("results", Inertia.Defer(() => _search.Page(page)).DeepMerge());      // deferred, then merged
result.With("activity", Inertia.Merge(() => _activity.Recent()).Once());          // merged and remembered
result.With("categories", Inertia.Optional(() => _categories.All()).Once());      // optional and remembered
```

A deferred prop that the client already remembers is not announced in `deferredProps` again.

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
