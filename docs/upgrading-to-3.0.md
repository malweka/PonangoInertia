# Upgrading to 3.0

Ponango.Inertia 3.0 aligns the adapter with the current [Inertia.js v3 protocol](https://inertiajs.com/docs/v3/core-concepts/the-protocol)
and the reference (Laravel) adapter. Most applications only need the client-side change for flash data. Read
each section below and check whether your app relies on the old behavior.

Client requirements:

- `@inertiajs/vue3`, `@inertiajs/react` or `@inertiajs/svelte` **3.x**
- **3.8.0 or later** if you enable big integer support

## 1. Flash data moved to `page.flash`

Flash values set with `WithFlash(...)` or `InertiaContext.Flash(...)` are now emitted in the page object's
top-level `flash` field instead of `props.flash`.

Before (2.x):

```json
{ "component": "Users/Index", "props": { "flash": { "success": "User created" }, "users": [] }, "sharedProps": ["flash"] }
```

After (3.0):

```json
{ "component": "Users/Index", "props": { "errors": {}, "users": [] }, "flash": { "success": "User created" } }
```

Update the client to read flash data from the page instead of its props:

```js
// Before
const { flash } = usePage().props

// After
const { flash } = usePage()

// Or react to it once per response
router.on('flash', (event) => showToast(event.detail.flash.success))
```

The client does not store flash data in browser history, so a message no longer reappears when the user
navigates back.

If you also share a prop named `flash` (`Share("flash", ...)`), it is now an ordinary prop and is no longer merged
with flashed values. Rename it or move those values to `WithFlash(...)`.

## 2. `errors` is always present

`props.errors` is now on every page, as `{}` when there are no errors. Client code that checked
`props.errors === undefined` should check for an empty object instead.

## 3. Partial reloads with only `except`

`router.reload({ except: ['users'] })` used to skip optional and deferred props entirely. Like the reference
adapter, it now resolves every prop that is not excluded, including optional and deferred ones. Full visits still
never resolve them. If an except-only reload should not load an expensive optional prop, add that prop to the
`except` list or use `only`.

When both `only` and `except` are sent, the except list is now applied (it used to be ignored).

## 4. Redirects with a `#fragment`

During Inertia requests the middleware now handles fragment redirects the way the protocol describes:

| Redirect target | 2.x | 3.0 |
|---|---|---|
| External URL with a fragment | `409` + `X-Inertia-Redirect` (the client tried to XHR another origin) | `409` + `X-Inertia-Location` (full page visit) |
| Internal URL with a fragment | plain `302`/`303` (the fragment was lost) | `409` + `X-Inertia-Redirect` (the client visits the full URL) |

No code change is needed unless you depended on the old headers.

## 5. Once props

- `onceProps[*].expiresAt` is now in **milliseconds**, as the client expects. With 2.x every once prop with an
  expiry was refetched on every visit; it is now cached until it expires.
- A once prop the client already holds keeps its `onceProps` entry (only the value is skipped), and partial
  reloads that request it always return a fresh value.

## 6. Infinite scroll: prefer `Inertia.Scroll(...)`

`MergeProp.WithScroll(...)` still works but is obsolete. It merges the whole prop at the root and only supports
page numbers. Its `scrollProps` entry now also carries `reset` and explicit `null` page values.

Move to `Inertia.Scroll(...)`, which merges the items under a `data` key (`mergeProps: ["posts.data"]`), supports
cursors, and can be deferred:

```csharp
// Before
result.With("posts", Inertia.Merge(() => page.Items).WithScroll(page.CurrentPage, page.PreviousPage, page.NextPage));

// After
result.With("posts", Inertia.Scroll(
    () => new { data = page.Items },
    ScrollMetadata.ForPage(page.CurrentPage, page.PreviousPage, page.NextPage)));
```

On the client, `<InfiniteScroll data="posts">` then reads `posts.data`.

The `X-Inertia-Infinite-Scroll-Merge-Intent` header now only affects scroll props. Plain merge props keep the
mode you configured.

## 7. Dotted top-level keys are unpacked

A top-level prop key containing dots is now unpacked into nested objects, as in the reference adapter:
`["auth.user"] = user` is sent as `{ "auth": { "user": ... } }` instead of a literal `"auth.user"` key. Rename such
keys if you relied on the literal form.

## 8. Binary compatibility

`Inertia.Defer(...)` and the `DeferredProp` constructors gained an optional `rescue` parameter. Code compiles
unchanged, but assemblies built against 2.x must be recompiled.

## New features worth adopting

- Combine prop types: `Inertia.Defer(...).Once()`, `Inertia.Defer(...).DeepMerge()`, `Inertia.Merge(...).Once()`,
  `Inertia.Optional(...).Once()`.
- Rescue failing deferred props: `Inertia.Defer(..., rescue: true)` and the client's `rescue` slot.
- Once options: `.As("key")`, `.Fresh()`, `.Until(...)`, and `InertiaContext.ShareOnce(...)`.
- Merge nested arrays: `Inertia.Merge(...).Append("data", matchOn: "id")`.
- Nested prop types with dot-notation reloads: `router.reload({ only: ['auth.notifications'] })`.
- Exact 64-bit integers: `options.PreserveBigIntegers = true`.
- Every validation message per field: `options.WithAllErrors = true`.
- Lazy props: `(Func<object>)(() => query.ToList())` is only evaluated when included.

See [advanced-topics.md](./advanced-topics.md) for details.
