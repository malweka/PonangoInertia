namespace Ponango.Inertia;

/// <summary>
/// Static factory methods for Inertia prop wrappers.
/// </summary>
public static class Inertia
{
    public static OptionalProp Optional(Func<object> callback) => new(callback);

    public static OptionalProp Optional(Func<Task<object>> callback) => new(callback);

    public static AlwaysProp Always(Func<object> callback) => new(callback);

    public static AlwaysProp Always(Func<Task<object>> callback) => new(callback);

    public static DeferredProp Defer(Func<object> callback, string group = "default") => new(callback, group);

    public static DeferredProp Defer(Func<Task<object>> callback, string group = "default") => new(callback, group);

    public static MergeProp Merge(Func<object> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    public static MergeProp Merge(Func<Task<object>> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    public static OnceProp Once(Func<object> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);

    public static OnceProp Once(Func<Task<object>> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);
}
