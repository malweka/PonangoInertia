# Ponango.Inertia

A .NET 8.0 server-side adapter for [Inertia.js](https://inertiajs.com/), enabling you to build modern single-page applications using classic server-side routing and controllers with client-side rendering frameworks like Vue, React, or Svelte.

## Documentation

- [Getting Started](./docs/getting-started.md)
- [Advanced Topics](./docs/advanced-topics.md)
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

## Features

- `Render(...)` as the primary rendering API
- `OptionalProp`, `AlwaysProp`, `DeferredProp`, `MergeProp`, and `OnceProp`
- partial reload support with `X-Inertia-Partial-Data`, `X-Inertia-Partial-Except`, and `X-Inertia-Reset`
- shared data, and one-time flash data emitted as the page-level `flash` field (backed by TempData)
- error bags and precognition
- history flags: `encryptHistory`, `clearHistory`, `preserveFragment`
- prefetch detection
- infinite scroll metadata via `scrollProps`
- external location responses via `Location(...)`

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

See the [Inertia.js documentation](https://inertiajs.com/) for client-side setup instructions.

## Upgrade Notes

- Add `app.UseInertia()` to the middleware pipeline.
- Prefer `Render(...)` over the older `Inertia(...)` helpers.
- Prefer `OptionalProp` over `LazyProp`.
- Read [docs/migration-from-v1.md](./docs/migration-from-v1.md) if you are upgrading an older integration.

## License

MIT
