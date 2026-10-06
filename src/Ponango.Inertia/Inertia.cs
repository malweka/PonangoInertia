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

    /// <summary>
    /// Defers the prop to a follow-up request. With <paramref name="rescue"/>, a failing callback omits the prop
    /// and lists it in <c>rescuedProps</c> instead of failing the response.
    /// </summary>
    public static DeferredProp Defer(Func<object> callback, string group = "default", bool rescue = false)
        => new(callback, group, rescue);

    /// <inheritdoc cref="Defer(Func{object}, string, bool)"/>
    public static DeferredProp Defer(Func<Task<object>> callback, string group = "default", bool rescue = false)
        => new(callback, group, rescue);

    public static MergeProp Merge(Func<object> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    public static MergeProp Merge(Func<Task<object>> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    /// <summary>Deep merges the whole value with existing client data.</summary>
    public static MergeProp DeepMerge(Func<object> callback) => new(callback, MergeMode.DeepMerge);

    /// <inheritdoc cref="DeepMerge(Func{object})"/>
    public static MergeProp DeepMerge(Func<Task<object>> callback) => new(callback, MergeMode.DeepMerge);

    public static OnceProp Once(Func<object> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);

    public static OnceProp Once(Func<Task<object>> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);
}
