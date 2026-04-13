# Migration From v1

This guide summarizes the changes you need to make when upgrading an older `Ponango.Inertia` integration to the current v3-oriented protocol implementation.

## Required middleware

Older setups often only registered services:

```csharp
builder.Services.AddInertia();
```

You should now also add:

```csharp
app.UseInertia();
```

This ensures:

- `Vary: X-Inertia` is applied consistently
- asset version mismatches return `409`
- shared data middleware behavior is applied
- external redirect handling works correctly

## Use `Render(...)` instead of `Inertia(...)`

Old:

```csharp
return _inertia.Inertia("Inertia", model, "Users/Index");
```

New:

```csharp
return _inertia.Render("Users/Index", new
{
    users = model.Users
});
```

The old `Inertia(...)` methods still exist, but they are obsolete.

## Initial HTML response format changed

Older integrations used a `data-page` attribute on the root element.

Current format:

```html
<div id="app"></div>
<script type="application/json" data-page data-inertia>{...}</script>
```

If your frontend bootstrap assumes the old attribute format, update it.

## `LazyProp` has been replaced by `OptionalProp`

Old:

```csharp
result.Props["analytics"] = new LazyProp(() => LoadAnalytics());
```

New:

```csharp
result.With("analytics", Inertia.Optional(() => LoadAnalytics()));
```

`LazyProp` still works for compatibility but is obsolete.

## New prop types are available

You can now use:

- `OptionalProp`
- `AlwaysProp`
- `DeferredProp`
- `OnceProp`
- `MergeProp`

These wrappers support partial reloads, deferred loading, infinite scroll, and client-side caching semantics.

## Shared props and flash data

If you previously injected shared data manually in each controller, consider moving common props to:

```csharp
builder.Services.AddInertia(options =>
{
    options.SharedData = ctx => new Dictionary<string, object>
    {
        ["appName"] = "My App"
    };
});
```

Flash messages can now be emitted directly from the response:

```csharp
return _inertia.Render("Users/Index", new { users })
    .WithFlash("success", "User created");
```

## Error bags and precognition

Validation handling now supports:

- error bags via `WithErrors(..., "bagName")`
- `X-Inertia-Error-Bag`
- precognition via `[Precognitive]`

If you previously used only flat `errors`, those flows still work.

## History and fragment flags

You can now signal:

- `encryptHistory`
- `clearHistory`
- `preserveFragment`

Example:

```csharp
return _inertia.Render("Auth/Login", new { })
    .WithClearHistory()
    .WithPreserveFragment();
```

## Recommended upgrade order

1. Add `app.UseInertia()`.
2. Update frontend bootstrap for the script-tag page payload.
3. Migrate controllers from `Inertia(...)` to `Render(...)`.
4. Replace `LazyProp` usage with `Inertia.Optional(...)`.
5. Adopt advanced wrappers only where needed.
