# Ponango.Inertia

A .NET 8.0 server-side adapter for [Inertia.js](https://inertiajs.com/), enabling you to build modern single-page applications using classic server-side routing and controllers with client-side rendering frameworks like Vue, React, or Svelte.

## Installation

```bash
dotnet add package Ponango.Inertia
```

## Project Structure

```
src/Ponango.Inertia/
├── InertiaResult.cs              # ActionResult handling Inertia responses
├── InertiaController.cs          # Base controller with Inertia helper methods
├── InertiaContext.cs             # Scoped service for request context and shared data
├── InertiaExtensions.cs          # Extension methods for InertiaContext
├── InertiaRequestHeaders.cs      # Request header model
├── InertiaRedirectResult.cs      # Redirect handling (303 for POST/PUT/PATCH/DELETE)
├── LazyProp.cs                   # Lazy-evaluated props for partial reloads
├── PageModel.cs                  # Inertia page object model
├── HtmlHelperExtensions.cs       # Razor view helpers
├── ServiceCollectionExtensions.cs # DI registration
└── IAssetVersionProvider.cs      # Asset versioning interface
```

## Setup

### 1. Register Services

```csharp
// Program.cs
builder.Services.AddInertia();

// Register your asset version provider
builder.Services.AddSingleton<IAssetVersionProvider, MyAssetVersionProvider>();
```

### 2. Implement Asset Version Provider

```csharp
public class MyAssetVersionProvider : IAssetVersionProvider
{
    public string GetAssetVersion()
    {
        // Return a version string that changes when your assets change
        // This triggers full page reloads when assets are updated
        return "1.0.0"; // Or use a hash of your manifest file
    }
}
```

### 3. Create a Razor Layout

```html
<!-- Views/Shared/_Layout.cshtml -->
@inject IJsonSerializerOptionBuilder Serializer
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>My App</title>
    @* Include your frontend assets (Vite, Webpack, etc.) *@
</head>
<body>
    @Html.InertiaRender(Serializer)
    <script src="/js/app.js"></script>
</body>
</html>
```

### 4. Create a Shared Inertia View

```html
<!-- Views/Shared/Inertia.cshtml -->
@{
    Layout = "_Layout";
}
```

## Usage

### Option 1: Using InertiaContext (Recommended)

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
        var data = new { Name = "John", Email = "john@example.com" };
        return _inertia.Inertia("Inertia", data, "Home/Index");
    }
}
```

### Option 2: Using InertiaController Base Class

```csharp
public class UsersController : InertiaController
{
    public IActionResult Index()
    {
        var users = _userService.GetAll();
        return Inertia("Inertia", new { Users = users });
        // Component name defaults to "Users" (from controller name)
    }

    public IActionResult Show(int id)
    {
        var user = _userService.Get(id);
        return Inertia("Inertia", new { User = user }, component: "Users/Show");
    }
}
```

## Features

### Shared Data

Share data across all Inertia responses:

```csharp
public class BaseController : Controller
{
    private readonly InertiaContext _inertia;

    public BaseController(InertiaContext inertia)
    {
        _inertia = inertia;
    }

    protected void ShareAuthData()
    {
        _inertia.Share("auth", new {
            User = GetCurrentUser(),
            Permissions = GetPermissions()
        });
    }
}
```

Or use middleware:

```csharp
app.Use(async (context, next) =>
{
    var inertia = context.RequestServices.GetRequiredService<InertiaContext>();
    inertia.Share("appName", "My Application");
    await next();
});
```

### Validation Errors

Return validation errors following Inertia conventions:

```csharp
[HttpPost]
public IActionResult Store(CreateUserRequest request)
{
    if (!ModelState.IsValid)
    {
        return _inertia.Inertia("Inertia", request, "Users/Create")
            .WithErrors(ModelState);
    }

    // Process valid request...
    return RedirectToAction("Index");
}
```

### Lazy Props

Defer expensive data loading until explicitly requested via partial reloads:

```csharp
public IActionResult Dashboard()
{
    var result = _inertia.Inertia("Inertia", new { }, "Dashboard");

    // Only loaded when explicitly requested in a partial reload
    result.Props["analytics"] = new LazyProp(() => _analyticsService.GetExpensiveReport());
    result.Props["notifications"] = new LazyProp(() => _notificationService.GetAll());

    return result;
}
```

### Partial Reloads

The adapter automatically handles partial reload requests via:
- `X-Inertia-Partial-Data`: Include only specified props
- `X-Inertia-Partial-Except`: Exclude specified props

The `errors` prop is always preserved in partial reloads per the Inertia protocol.

### Custom JSON Serialization

Configure JSON serialization options:

```csharp
builder.Services.AddInertia(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.Converters.Add(new JsonStringEnumConverter());
});
```

## How It Works

1. **Initial Page Load**: Returns a full HTML page with the Inertia page object embedded in a `data-page` attribute on the root element.

2. **Subsequent Requests**: When the `X-Inertia` header is present, returns only JSON containing the component name, props, URL, and version.

3. **Asset Versioning**: Compares `X-Inertia-Version` header with current version. On mismatch, returns `409 Conflict` with `X-Inertia-Location` header to trigger a full page reload.

4. **Redirects**: POST/PUT/PATCH/DELETE requests that redirect use `303 See Other` status to ensure the browser follows with a GET request.

## Frontend Setup

Pair this server-side adapter with the official Inertia.js client adapter for your framework:

- **Vue**: `@inertiajs/vue3`
- **React**: `@inertiajs/react`
- **Svelte**: `@inertiajs/svelte`

See the [Inertia.js documentation](https://inertiajs.com/) for client-side setup instructions.

## License

MIT
