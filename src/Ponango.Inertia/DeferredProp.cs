namespace Ponango.Inertia;

/// <summary>
/// A prop that is excluded from the initial page response and loaded by the client
/// in a follow-up partial reload request. Deferred props can be grouped so that
/// multiple props in the same group are fetched in a single request.
/// Deferred props can also merge (<see cref="Merge"/>, <see cref="DeepMerge"/>), be remembered by the client
/// (<see cref="Once"/>) and rescue resolution errors (<see cref="Rescue"/>).
/// </summary>
public class DeferredProp : IResolvableProp, IIgnoreFirstLoad, IDeferrableProp, IMergeableProp, IOnceableProp, IRescuableProp
{
    private readonly Func<Task<object>> _callback;
    private readonly MergeBehavior _merge = new();
    private readonly OnceBehavior _once = new();
    private bool _rescue;

    /// <summary>
    /// The group name for this deferred prop. Props in the same group are fetched together.
    /// Defaults to "default".
    /// </summary>
    public string Group { get; }

    public DeferredProp(Func<object> callback, string group = "default", bool rescue = false)
        : this(WrapSync(callback), group, rescue)
    {
    }

    public DeferredProp(Func<Task<object>> callback, string group = "default", bool rescue = false)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        Group = string.IsNullOrWhiteSpace(group) ? "default" : group;
        _rescue = rescue;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>
    /// When the callback throws, omit the prop, log the exception, and list the key in <c>rescuedProps</c>
    /// so the client can render the <c>rescue</c> slot of its <c>Deferred</c> component.
    /// </summary>
    public DeferredProp Rescue(bool rescue = true)
    {
        _rescue = rescue;
        return this;
    }

    /// <summary>Appends the loaded value to existing client data at the root.</summary>
    public DeferredProp Merge()
    {
        _merge.AppendAtRoot();
        return this;
    }

    /// <summary>Deep merges the loaded value with existing client data.</summary>
    public DeferredProp DeepMerge()
    {
        _merge.DeepMerge();
        return this;
    }

    /// <summary>Appends at the root. Same as <see cref="Merge"/>.</summary>
    public DeferredProp Append()
    {
        _merge.AppendAtRoot();
        return this;
    }

    /// <summary>Appends the array at <paramref name="path"/> (relative to the prop), optionally matching items on a field.</summary>
    public DeferredProp Append(string path, string? matchOn = null)
    {
        _merge.AppendAt(path, matchOn);
        return this;
    }

    /// <summary>Appends the arrays at each of <paramref name="paths"/>.</summary>
    public DeferredProp Append(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) _merge.AppendAt(path, null);
        return this;
    }

    /// <summary>Appends the arrays at each path, matching items on the given field (value may be null).</summary>
    public DeferredProp Append(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) _merge.AppendAt(entry.Key, entry.Value);
        return this;
    }

    /// <summary>Prepends at the root.</summary>
    public DeferredProp Prepend()
    {
        _merge.PrependAtRoot();
        return this;
    }

    /// <summary>Prepends the array at <paramref name="path"/> (relative to the prop), optionally matching items on a field.</summary>
    public DeferredProp Prepend(string path, string? matchOn = null)
    {
        _merge.PrependAt(path, matchOn);
        return this;
    }

    /// <summary>Prepends the arrays at each of <paramref name="paths"/>.</summary>
    public DeferredProp Prepend(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) _merge.PrependAt(path, null);
        return this;
    }

    /// <summary>Prepends the arrays at each path, matching items on the given field (value may be null).</summary>
    public DeferredProp Prepend(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) _merge.PrependAt(entry.Key, entry.Value);
        return this;
    }

    /// <summary>
    /// Sets the fields (relative to the prop, e.g. <c>id</c> or <c>messages.id</c>) used to match existing items
    /// when merging. Replaces any match fields set earlier.
    /// </summary>
    public DeferredProp MatchingOn(params string[] fields)
    {
        _merge.SetMatchOn(fields);
        return this;
    }

    /// <summary>
    /// Remembers the value on the client once it has been loaded. A remembered deferred prop is no longer
    /// announced in <c>deferredProps</c>.
    /// </summary>
    public DeferredProp Once(bool once = true, string? @as = null, TimeSpan? until = null)
    {
        _once.ShouldResolveOnce = once;
        if (@as != null) _once.As(@as);
        if (until.HasValue) _once.Until(until.Value);
        return this;
    }

    /// <summary>Remembers the value under a custom key shared across pages. Implies <see cref="Once"/>.</summary>
    public DeferredProp As(string key)
    {
        _once.As(key);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public DeferredProp Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>. Implies <see cref="Once"/>.</summary>
    public DeferredProp Until(TimeSpan duration)
    {
        _once.Until(duration);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>. Implies <see cref="Once"/>.</summary>
    public DeferredProp Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        _once.ShouldResolveOnce = true;
        return this;
    }

    static Func<Task<object>> WrapSync(Func<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return () => Task.FromResult(callback());
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();

    bool IDeferrableProp.ShouldDefer => true;

    bool IMergeableProp.ShouldMerge => _merge.ShouldMerge;
    bool IMergeableProp.ShouldDeepMerge => _merge.ShouldDeepMerge;
    bool IMergeableProp.AppendsAtRoot => _merge.AppendsAtRoot;
    bool IMergeableProp.PrependsAtRoot => _merge.PrependsAtRoot;
    IReadOnlyList<string> IMergeableProp.AppendPaths => _merge.AppendPaths;
    IReadOnlyList<string> IMergeableProp.PrependPaths => _merge.PrependPaths;
    IReadOnlyList<string> IMergeableProp.MatchOnPaths => _merge.MatchOnPaths;

    bool IOnceableProp.ShouldResolveOnce => _once.ShouldResolveOnce;
    bool IOnceableProp.ShouldBeRefreshed => _once.ShouldBeRefreshed;
    string? IOnceableProp.OnceKey => _once.OnceKey;
    long? IOnceableProp.ExpiresAtMilliseconds() => _once.ExpiresAtMilliseconds();

    bool IRescuableProp.ShouldRescue => _rescue;
}
