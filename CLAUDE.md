# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ponango.Inertia is a .NET 8.0 implementation of Inertia.js for ASP.NET Core applications. It enables building modern single-page applications using server-side routing and controllers with client-side rendering frameworks.

## Build and Development Commands

```bash
# Build the project
dotnet build src/Ponango.Inertia/Ponango.Inertia.csproj

# Build for release (generates NuGet package)
dotnet build src/Ponango.Inertia/Ponango.Inertia.csproj -c Release

# Build from solution file
dotnet build src/Ponango.Inertia/Ponango.Inertia.sln

# Pack the NuGet package
dotnet pack src/Ponango.Inertia/Ponango.Inertia.csproj -c Release
```

## Architecture

### Core Request/Response Flow

1. **Request Detection**: `InertiaContext` checks for `X-Inertia` header to determine if request is from Inertia client
2. **Asset Versioning**: `IAssetVersionProvider` provides version hash for cache-busting; version mismatches return 409 with `X-Inertia-Location` header
3. **Response Generation**:
   - Non-Inertia requests: Returns standard MVC view with PageModel injected as `data-page` attribute via `InertiaRender()` helper
   - Inertia requests: Returns JSON response with component name, URL, version, and props

### Key Components

**InertiaResult** (src/Ponango.Inertia/InertiaResult.cs:13)
- ActionResult implementation that handles both full page loads and Inertia requests
- Manages partial reloads by filtering props based on `X-Inertia-Partial-Data` and `X-Inertia-Partial-Component` headers
- Handles lazy props (only evaluated when requested in partial reloads)
- Merges shared props from InertiaContext with page-specific props

**InertiaContext** (src/Ponango.Inertia/InertiaContext.cs:6)
- Scoped service providing access to Inertia request headers and shared data
- `Share()` method allows sharing data across all Inertia responses
- `SharedProps` dictionary is merged into every InertiaResult response
- Access via dependency injection in controllers or middleware

**InertiaController** (src/Ponango.Inertia/InertiaController.cs:6)
- Base controller with `Inertia<T>()` helper method
- Auto-derives component name from controller name (strips "Controller" suffix)
- Auto-generates asset version from model type's assembly version
- Overrides `Redirect()` to return InertiaRedirectResult for proper HTTP status codes

**InertiaRedirectResult** (src/Ponango.Inertia/InertiaRedirectResult.cs:9)
- Returns 303 status code for POST/PUT/PATCH/DELETE redirects (Inertia protocol requirement)
- Preserves standard behavior for GET requests and permanent redirects

**LazyProp** (src/Ponango.Inertia/LazyProp.cs:5)
- Wrapper for expensive computations that should only run when explicitly requested
- Only evaluated during partial reloads when the prop is in `X-Inertia-Partial-Data`
- Removed from full visits and non-matching partial visits

### Partial Reloads and Lazy Props

Partial reload logic (InertiaResult.cs:139-181):
1. Check if request has `X-Inertia-Partial-Component` matching current component
2. If match, filter props to only those in `X-Inertia-Partial-Data` header
3. LazyProps are included ONLY in matching partial reloads where they're requested
4. Full visits and non-matching partials remove all LazyProps before evaluation

### Validation Errors

The `WithErrors()` method (InertiaResult.cs:41) converts ModelStateDictionary to flat key-value pairs and adds them to props as `errors` key, following Inertia.js conventions.

### JSON Serialization

- Customizable via `AddInertia()` service registration
- Default: camelCase naming, ignores null values, handles reference cycles
- `IJsonSerializerOptionBuilder` allows runtime configuration

## Usage Patterns

### Standard Controller Pattern
```csharp
public class HomeController : InertiaController
{
    public IActionResult Index(MyModel model)
    {
        // Component defaults to "Home", version auto-generated
        return Inertia("Index", model);
    }
}
```

### Extension Method Pattern (Recommended)
```csharp
public class HomeController : Controller
{
    private readonly InertiaContext _inertia;

    public HomeController(InertiaContext inertia)
    {
        _inertia = inertia;
    }

    public IActionResult Index(MyModel model)
    {
        return _inertia.Inertia("Index", model, "Home/Index", propsName: "data");
    }
}
```

### Shared Data Pattern
```csharp
// In middleware or controller
_inertiaContext.Share("auth", new { user = currentUser });
```

### Lazy Props Pattern
```csharp
var result = _inertia.Inertia("Dashboard", model, "Dashboard");
result.Props["expensiveData"] = new LazyProp(() => LoadExpensiveData());
```

## Service Registration

```csharp
services.AddInertia(options => {
    // Customize JSON serialization if needed
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Register custom asset version provider
services.AddSingleton<IAssetVersionProvider, MyVersionProvider>();
```

## View Integration

In your Razor layout:
```cshtml
@inject IJsonSerializerOptionBuilder Serializer
<body>
    @Html.InertiaRender(Serializer, appId: "app")
</body>
```

This renders: `<div id="app" data-page="{...encoded JSON...}"></div>`

## Important Implementation Notes

- Asset version provider is REQUIRED - must be registered as a service implementing `IAssetVersionProvider`
- InertiaContext is scoped per request
- Props are case-sensitive on the C# side but serialized to camelCase by default
- Redirect after POST/PUT/DELETE uses 303 status (not 302) per Inertia protocol
- Version mismatches trigger full page reload via 409 response with `X-Inertia-Location` header
