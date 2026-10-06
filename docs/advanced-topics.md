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

Use `Inertia.Scroll(...)` for props rendered by the client's `<InfiniteScroll>` component. The prop value is
an object holding the page's items in a wrapper key (`data` by default) plus anything else you want to send:

```csharp
var page = await _posts.GetPageAsync(pageNumber, pageSize: 20);

return _inertia.Render("Posts/Index", new
{
    posts = Inertia.Scroll(
        () => new { data = page.Items, total = page.Total },
        ScrollMetadata.ForPage(page.CurrentPage, page.PreviousPage, page.NextPage))
});
```

This emits the items' merge label and the pagination state:

```json
{
  "props": { "errors": {}, "posts": { "data": [ ... ], "total": 120 } },
  "mergeProps": ["posts.data"],
  "scrollProps": { "posts": { "pageName": "page", "previousPage": null, "nextPage": 2, "currentPage": 1, "reset": false } }
}
```

Behavior:

- Items under the wrapper are appended. When the client loads earlier pages it sends
  `X-Inertia-Infinite-Scroll-Merge-Intent: prepend`, and they are prepended instead (`prependProps`).
- When the client resets the list (for example after a filter change, `router.reload({ reset: ['posts'] })`), the
  prop is returned without a merge label and `scrollProps.posts.reset` is `true`.
- `MatchingOn("id")` updates existing items in place (`matchPropsOn: ["posts.data.id"]`).
- `wrapper: "items"` changes the wrapper key (`posts.items`).

Cursor pagination uses string cursors:

```csharp
posts = Inertia.Scroll(() => new { data = slice.Items },
    ScrollMetadata.ForCursor(slice.Cursor, slice.PreviousCursor, slice.NextCursor, cursorName: "cursor"))
```

When the metadata depends on the loaded value, pass a callback. It receives the resolved value, and the value
callback runs only once:

```csharp
users = Inertia.Scroll(() => _users.Page(pageNumber),
    value => { var p = (UserPage)value; return ScrollMetadata.ForPage(p.Page, p.Previous, p.Next, pageName: "users"); })
```

Use a distinct `pageName` for each scroll container on the same page (for example `users` and `orders`) so
their query string parameters don't collide.

`.Defer(group)` loads the prop in a follow-up request: the full visit announces it in `deferredProps` and emits
its merge label, but no `scrollProps` until it is loaded.

### Legacy `MergeProp.WithScroll(...)`

`Inertia.Merge(...).WithScroll(...)` still works but is obsolete. It merges the whole prop value at the root
(`mergeProps: ["posts"]`), only supports page numbers, and its `scrollProps` entry now also includes `reset` and
explicit `null` page values. Move to `Inertia.Scroll(...)`, wrapping the items in a `data` key.

## Big integers

JavaScript rounds integers outside its safe range (±9,007,199,254,740,991) when it parses JSON, so a 64-bit ID
such as `900719925474099988` reaches your components as `900719925474100000`. Enable big integer support to
deliver those values exactly, as native `BigInt` values:

```csharp
builder.Services.AddInertia(options =>
{
    options.PreserveBigIntegers = true;
});
```

Or per response, which also lets you opt a single response out when it is enabled globally:

```csharp
return _inertia.Render("Orders/Show", new { order }).WithPreserveBigIntegers();
return _inertia.Render("Reports/Index", props).WithPreserveBigIntegers(false);
```

When enabled, `long`, `ulong`, `Int128`, `UInt128` and `BigInteger` values outside the safe range, in props and
flash data (including values held in `JsonElement`s), are written as markers, and the page is flagged:

```json
{ "props": { "errors": {}, "id": { "$bigint": "900719925474099988" }, "count": 3 }, "preserveBigIntegers": true }
```

Values inside the safe range stay plain numbers, so the same prop may arrive as a `number` or a `BigInt`
depending on its value. `decimal` and `double` values are never converted. Dictionary keys are JSON strings
already and keep their digits. The marker converters take precedence over any converter you register for these
types.

Requirements and notes:

- The client revives markers only on pages flagged with `preserveBigIntegers`, and only from
  `@inertiajs/vue3`, `@inertiajs/react` or `@inertiajs/svelte` **3.8.0** or later.
- A `BigInt` submitted through the router, a form or Precognition is sent as its digits, so it binds to a `long`
  action parameter or model property like any other number.
- The `useHttp` hook does not handle `BigInt`; convert those values to strings before sending them.

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
