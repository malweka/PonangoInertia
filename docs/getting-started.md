# Getting Started

This guide gets a new ASP.NET Core application up and running with `Ponango.Inertia`.

## 1. Install the package

```bash
dotnet add package Ponango.Inertia
```

## 2. Register Inertia services

In `Program.cs`, register Inertia, your asset version provider, and the middleware.

```csharp
using Ponango.Inertia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddInertia(options =>
{
    options.RootView = "Inertia";
});

builder.Services.AddSingleton<IAssetVersionProvider, AssetVersionProvider>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseInertia();
app.MapDefaultControllerRoute();

app.Run();
```

## 3. Add an asset version provider

The asset version is used for Inertia version checks. When it changes, the client performs a full reload.

```csharp
using Ponango.Inertia;

public sealed class AssetVersionProvider : IAssetVersionProvider
{
    public string GetAssetVersion() => "1.0.0";
}
```

In a real app, this is usually a manifest hash, commit SHA, or a build version.

## 4. Create the shared Razor view

Create `Views/Shared/Inertia.cshtml`:

```cshtml
@{
    Layout = "_Layout";
}
```

## 5. Render the Inertia payload in your layout

Update `Views/Shared/_Layout.cshtml`:

```cshtml
@inject IJsonSerializerOptionBuilder Serializer
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>My App</title>
</head>
<body>
    @Html.InertiaRender(Serializer)
    <script src="/js/app.js"></script>
</body>
</html>
```

`Ponango.Inertia` emits the page object using the v3 script-tag format:

```html
<div id="app"></div>
<script type="application/json" data-page="app" data-inertia>{...}</script>
```

## 6. Add a controller endpoint

The preferred API is `Render(...)`.

```csharp
using Microsoft.AspNetCore.Mvc;
using Ponango.Inertia;

public class UsersController : Controller
{
    private readonly InertiaContext _inertia;

    public UsersController(InertiaContext inertia)
    {
        _inertia = inertia;
    }

    [HttpGet("/users")]
    public IActionResult Index()
    {
        var users = new[]
        {
            new { Id = 1, Name = "Alice" },
            new { Id = 2, Name = "Bob" }
        };

        return _inertia.Render("Users/Index", new
        {
            users
        });
    }
}
```

## 7. Return validation errors

Use `WithErrors(...)` on the returned `InertiaResult`.

```csharp
[HttpPost("/users")]
public IActionResult Store(CreateUserRequest request)
{
    if (!ModelState.IsValid)
    {
        return _inertia.Render("Users/Create", new { form = request })
            .WithErrors(ModelState);
    }

    return RedirectToAction(nameof(Index));
}
```

## 8. Share global props

For application-wide props, configure `SharedData`:

```csharp
builder.Services.AddInertia(options =>
{
    options.SharedData = httpContext => new Dictionary<string, object>
    {
        ["appName"] = "My Application"
    };
});
```

Or share data inside a controller or middleware:

```csharp
_inertia.Share("auth", new
{
    user = User.Identity?.Name
});
```

## 9. Use the controller base class if you prefer

`InertiaController` exposes `Render(...)`, `Location(...)`, and request flags like `IsInertia`.

```csharp
using Ponango.Inertia;

public class DashboardController : InertiaController
{
    public IActionResult Index()
    {
        return Render("Dashboard/Index", new
        {
            message = "Hello"
        });
    }
}
```

## 10. Connect your frontend adapter

Pair this package with the official Inertia client library for your frontend:

- Vue: `@inertiajs/vue3`
- React: `@inertiajs/react`
- Svelte: `@inertiajs/svelte`

Your frontend should read the Inertia page object from the initial HTML response and issue subsequent Inertia requests with the standard headers.

## Next steps

- Read [advanced-topics.md](./advanced-topics.md) for deferred props, partial reloads, shared data, flash messages, error bags, precognition, prefetch, and infinite scroll.
- Read [migration-from-v1.md](./migration-from-v1.md) if you are upgrading an older integration.
