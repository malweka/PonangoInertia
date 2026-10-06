# Ponango.Inertia

A .NET 8.0 server-side adapter for [Inertia.js](https://inertiajs.com/), enabling you to build modern single-page applications using classic server-side routing and controllers with client-side rendering frameworks like Vue, React, or Svelte.

## Documentation

- [Getting Started](./docs/getting-started.md)
- [Advanced Topics](./docs/advanced-topics.md)
- [Compatibility with Inertia.js](./docs/compatibility.md)
- [Upgrading to 3.0](./docs/upgrading-to-3.0.md)
- [Migration From v1](./docs/migration-from-v1.md)
- [Changelog](./CHANGELOG.md)

## Installation

```bash
dotnet add package Ponango.Inertia
```

## Quick Start

```csharp
// Program.cs
builder.Services.AddControllersWithViews();

builder.Services.AddInertia(options =>
{
    options.RootView = "Inertia";
});

builder.Services.AddSingleton<IAssetVersionProvider, AssetVersionProvider>();

app.UseRouting();
app.UseInertia();
app.MapDefaultControllerRoute();
```

Preferred controller API:

```csharp
public class HomeController : Controller
{
    private readonly InertiaContext _inertia;

    public HomeController(InertiaContext inertia)
    {
        _inertia = inertia;
    }

    public IActionResult Index()
    {
        return _inertia.Render("Home/Index", new
        {
            name = "John",
            email = "john@example.com"
        });
    }
}
```

See [docs/getting-started.md](./docs/getting-started.md) for the full setup: package install, asset versioning, Razor layout, shared Inertia view, and a working endpoint.

## What Is Inertia.js?

Inertia lets you build a single-page app without building an API. Your ASP.NET Core controllers keep doing the
routing, authorization, validation and data loading. Instead of returning Razor HTML, an action returns the name
of a client-side page component and its props. Inertia renders that component with Vue, React or Svelte, and
later navigations swap pages through small JSON requests instead of full page loads.

The main Inertia features:

- **Server-driven routing.** Links and forms trigger visits that call your controllers. No client router or REST
  layer to maintain.
- **Pages and layouts.** Each response names a page component; persistent layouts survive navigation.
- **Forms and validation.** `useForm` and `<Form>` submit to your actions, and validation errors come back as
  props.
- **Partial reloads.** Ask the server for only some props (`router.reload({ only: ['users'] })`).
- **Deferred, lazy, merged and once props.** Load slow data after the first render, append pages of results,
  and cache rarely changing data on the client.
- **Infinite scroll, polling, prefetching, load-when-visible** and instant visits.
- **Shared data and flash messages** available on every page.
- **History management.** Back/forward restores state; history entries can be encrypted.
- **Asset versioning.** Clients reload automatically after a deployment.
- **Precognition.** Validate a form live without running the action.

## What This Adapter Gives You

Ponango.Inertia implements the server side of the Inertia v3 protocol for ASP.NET Core:

- **Rendering:** `Render("Users/Index", props)` from any controller, `InertiaController` helpers, and a Razor
  helper that writes the initial page payload.
- **Middleware (`UseInertia()`):** asset version checks, `Vary` headers, `303` redirects after form posts,
  external and `#fragment` redirects, and shared data.
- **Every v3 prop type, combinable:** `Optional`, `Always`, `Defer` (grouped, rescuable), `Merge` / `DeepMerge`
  (root or nested paths, match fields), `Once` (expiry, custom keys, forced refresh), `Scroll` (page numbers or
  cursors), plus lazy `Func<object>` props. For example `Inertia.Defer(...).Once()` or
  `Inertia.Defer(...).DeepMerge()`.
- **Nested props and dot notation:** wrappers inside nested objects, reloadable with
  `only: ['auth.notifications']`.
- **Shared data and flash:** app-wide props through options or per request, `ShareOnce`, and flash data in
  `page.flash` that survives redirects.
- **Validation:** `WithErrors(ModelState)`, error bags, optional all-messages-per-field, and `[Precognitive]`
  actions.
- **History and navigation:** encrypt or clear history, preserve URL fragments, detect prefetch requests.
- **Big integers:** 64-bit IDs delivered to the browser as exact `BigInt` values.

See [docs/compatibility.md](./docs/compatibility.md) for a feature-by-feature table against the current Inertia
release, including what isn't supported yet.

## Example APIs

```csharp
public class UsersController : InertiaController
{
    public IActionResult Index()
    {
        return Render("Users/Index", new
        {
            users = _userService.GetAll()
        });
    }

    [Precognitive]
    public IActionResult Store(CreateUserRequest request)
    {
        if (!ModelState.IsValid)
        {
            return Render("Users/Create", new { form = request })
                .WithErrors(ModelState)
                .WithFlash("error", "Validation failed");
        }

        return RedirectToAction(nameof(Index));
    }
}
```

Advanced prop wrappers:

```csharp
var result = _inertia.Render("Dashboard/Index", new { user });
result.With("permissions", Inertia.Always(() => GetPermissions()));
result.With("analytics", Inertia.Optional(() => GetAnalytics()));
result.With("report", Inertia.Defer(() => BuildReport(), group: "dashboard", rescue: true));
result.With("stats", Inertia.Defer(() => BuildStats()).Once());
result.With("plans", Inertia.Once(() => GetPlans()).Until(TimeSpan.FromHours(1)));
result.With("feed", Inertia.Merge(() => feed).Append("data", matchOn: "id"));
result.With("posts", Inertia.Scroll(
    () => page,
    ScrollMetadata.ForPage(page.CurrentPage, page.PreviousPage, page.NextPage)));
```

## Configuration

```csharp
builder.Services.AddInertia(options =>
{
    options.RootView = "Inertia";
    options.EncryptHistory = true;
    options.PreserveBigIntegers = true; // 64-bit IDs arrive as BigInt (client 3.8.0+)
    options.WithAllErrors = false;      // true: every validation message per field, as an array
    options.ExposeSharedPropKeys = true; // list shared keys in sharedProps (used by instant visits)
    options.SharedData = ctx => new Dictionary<string, object>
    {
        ["appName"] = "My Application"
    };
    options.JsonSerializerOptions = json =>
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    };
});
```

## How It Works

1. Initial page loads render a Razor view whose payload is emitted as a JSON script tag.
2. Inertia requests return JSON page objects.
3. Asset version mismatches return `409` with `X-Inertia-Location` and `X-Inertia-Version`.
4. External redirects return `409` with `X-Inertia-Location`; internal redirects to a URL with a `#fragment`
   return `409` with `X-Inertia-Redirect`.
5. Partial reload headers drive prop filtering and wrapper resolution.
6. Flash values are emitted in the page object's top-level `flash` field (`usePage().flash` on the client)
   and cleared after they are read.

## Frontend Setup

Pair this server-side adapter with the official Inertia.js client adapter for your framework:

- **Vue**: `@inertiajs/vue3`
- **React**: `@inertiajs/react`
- **Svelte**: `@inertiajs/svelte`

Use version 3.x of the client adapter (3.8.0 or later for big integer support).

See the [Inertia.js documentation](https://inertiajs.com/) for client-side setup instructions.

## Upgrade Notes

- Upgrading from 2.x? Read [docs/upgrading-to-3.0.md](./docs/upgrading-to-3.0.md). Flash data moved to
  `page.flash`, and several protocol details now match Inertia v3.
- Add `app.UseInertia()` to the middleware pipeline.
- Prefer `Render(...)` over the older `Inertia(...)` helpers.
- Prefer `OptionalProp` over `LazyProp`.
- Read [docs/migration-from-v1.md](./docs/migration-from-v1.md) if you are upgrading an older integration.

## Not Supported Yet

These Inertia v3 server features are not implemented:

- server-side rendering (SSR)
- the DevTools server protocol
- an Inertia-aware exception/error-page helper
- `ProvidesInertiaProperty` / `ProvidesInertiaProperties`-style prop provider interfaces
- testing helpers like Laravel's `assertInertia`

Validation errors are returned by re-rendering the page with `WithErrors(...)`; they are not carried across a
redirect. CSRF protection uses ASP.NET Core antiforgery, which you configure yourself. See
[docs/compatibility.md](./docs/compatibility.md) for details.

## License

MIT
