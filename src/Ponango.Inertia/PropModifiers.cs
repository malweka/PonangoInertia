namespace Ponango.Inertia;

/// <summary>
/// The once modifiers shared by <see cref="OptionalProp"/>, <see cref="DeferredProp"/> and <see cref="MergeProp"/>.
/// Every modifier returns the prop itself, so calls can be chained.
/// </summary>
/// <typeparam name="TSelf">The concrete prop type.</typeparam>
public abstract class OnceModifiers<TSelf> : IOnceableProp
    where TSelf : OnceModifiers<TSelf>
{
    private readonly OnceBehavior _once = new();

    private protected OnceModifiers()
    {
    }

    /// <summary>Remembers the value on the client once it has been loaded, so later responses skip it.</summary>
    public TSelf Once(bool once = true, string? @as = null, TimeSpan? until = null)
    {
        _once.ShouldResolveOnce = once;
        if (@as != null) _once.As(@as);
        if (until.HasValue) _once.Until(until.Value);
        return (TSelf)this;
    }

    /// <summary>Remembers the value under a custom key shared across pages. Implies <see cref="Once"/>.</summary>
    public TSelf As(string key)
    {
        _once.As(key);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public TSelf Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return (TSelf)this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>. Implies <see cref="Once"/>.</summary>
    public TSelf Until(TimeSpan duration)
    {
        _once.Until(duration);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>. Implies <see cref="Once"/>.</summary>
    public TSelf Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    bool IOnceableProp.ShouldResolveOnce => _once.ShouldResolveOnce;
    bool IOnceableProp.ShouldBeRefreshed => _once.ShouldBeRefreshed;
    string? IOnceableProp.OnceKey => _once.OnceKey;
    long? IOnceableProp.ExpiresAtMilliseconds() => _once.ExpiresAtMilliseconds();
}

/// <summary>
/// The merge modifiers shared by <see cref="DeferredProp"/> and <see cref="MergeProp"/>, on top of the once
/// modifiers.
/// </summary>
/// <typeparam name="TSelf">The concrete prop type.</typeparam>
public abstract class MergeModifiers<TSelf> : OnceModifiers<TSelf>, IMergeableProp
    where TSelf : MergeModifiers<TSelf>
{
    private protected MergeModifiers()
    {
    }

    private protected MergeBehavior MergeState { get; } = new();

    /// <summary>Deep merges the value with existing client data.</summary>
    public TSelf DeepMerge()
    {
        MergeState.DeepMerge();
        return (TSelf)this;
    }

    /// <summary>Appends the value to existing client data at the root.</summary>
    public TSelf Append()
    {
        MergeState.AppendAtRoot();
        return (TSelf)this;
    }

    /// <summary>
    /// Appends only the array at <paramref name="path"/> (relative to the prop) and replaces the rest of the value,
    /// optionally matching items on a field. Emits <c>mergeProps: ["prop.path"]</c>.
    /// </summary>
    public TSelf Append(string path, string? matchOn = null)
    {
        MergeState.AppendAt(path, matchOn);
        return (TSelf)this;
    }

    /// <summary>Appends the arrays at each of <paramref name="paths"/>.</summary>
    public TSelf Append(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) MergeState.AppendAt(path, null);
        return (TSelf)this;
    }

    /// <summary>Appends the arrays at each path, matching items on the given field (value may be null).</summary>
    public TSelf Append(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) MergeState.AppendAt(entry.Key, entry.Value);
        return (TSelf)this;
    }

    /// <summary>Prepends the value to existing client data at the root.</summary>
    public TSelf Prepend()
    {
        MergeState.PrependAtRoot();
        return (TSelf)this;
    }

    /// <summary>Prepends only the array at <paramref name="path"/> (relative to the prop), optionally matching items on a field.</summary>
    public TSelf Prepend(string path, string? matchOn = null)
    {
        MergeState.PrependAt(path, matchOn);
        return (TSelf)this;
    }

    /// <summary>Prepends the arrays at each of <paramref name="paths"/>.</summary>
    public TSelf Prepend(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) MergeState.PrependAt(path, null);
        return (TSelf)this;
    }

    /// <summary>Prepends the arrays at each path, matching items on the given field (value may be null).</summary>
    public TSelf Prepend(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) MergeState.PrependAt(entry.Key, entry.Value);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the fields (relative to the prop, e.g. <c>id</c> or <c>messages.id</c>) used to match existing items
    /// when merging. Replaces any match fields set earlier.
    /// </summary>
    public TSelf MatchingOn(params string[] fields)
    {
        MergeState.SetMatchOn(fields);
        return (TSelf)this;
    }

    bool IMergeableProp.ShouldMerge => MergeState.ShouldMerge;
    bool IMergeableProp.ShouldDeepMerge => MergeState.ShouldDeepMerge;
    bool IMergeableProp.AppendsAtRoot => MergeState.AppendsAtRoot;
    bool IMergeableProp.PrependsAtRoot => MergeState.PrependsAtRoot;
    IReadOnlyList<string> IMergeableProp.AppendPaths => MergeState.AppendPaths;
    IReadOnlyList<string> IMergeableProp.PrependPaths => MergeState.PrependPaths;
    IReadOnlyList<string> IMergeableProp.MatchOnPaths => MergeState.MatchOnPaths;
}
