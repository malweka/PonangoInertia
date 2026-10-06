namespace Ponango.Inertia;

/// <summary>
/// Controls how the client merges data during navigation.
/// </summary>
public enum MergeMode
{
    /// <summary>Append new items to the existing array.</summary>
    Append,
    /// <summary>Prepend new items before the existing array.</summary>
    Prepend,
    /// <summary>Deep merge objects recursively.</summary>
    DeepMerge
}

/// <summary>
/// A prop that instructs the client to merge its value with existing data
/// instead of replacing it. Used for "load more" lists, real-time feeds, etc.
/// Merging can target the root value or nested paths (<see cref="Append(string, string?)"/>).
/// </summary>
public class MergeProp : IResolvableProp, IMergeableProp, IOnceableProp, IMergeIntentAware, IScrollMetadataProvider
{
    private readonly Func<Task<object>> _callback;
    private readonly MergeBehavior _merge = new();
    private readonly OnceBehavior _once = new();

    /// <summary>
    /// How the client merges this prop's root value.
    /// </summary>
    public MergeMode Mode => _merge.ShouldDeepMerge
        ? MergeMode.DeepMerge
        : _merge.PrependsAtRoot ? MergeMode.Prepend : MergeMode.Append;

    /// <summary>
    /// The match key given to the constructor (e.g., "id").
    /// Serialized into the matchPropsOn array as "propKey.matchOn".
    /// </summary>
    public string? MatchOn { get; }

    /// <summary>
    /// Optional pagination metadata for infinite scroll responses.
    /// When present, InertiaResult emits a scrollProps entry for this prop key.
    /// </summary>
    public ScrollPropConfig? ScrollConfig { get; private set; }

    public MergeProp(Func<object> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        : this(WrapSync(callback), mode, matchOn)
    {
    }

    public MergeProp(Func<Task<object>> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        MatchOn = matchOn;

        switch (mode)
        {
            case MergeMode.DeepMerge:
                _merge.DeepMerge();
                break;
            case MergeMode.Prepend:
                _merge.PrependAtRoot();
                break;
            default:
                _merge.AppendAtRoot();
                break;
        }

        if (!string.IsNullOrWhiteSpace(matchOn))
            _merge.SetMatchOn(new[] { matchOn });
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>Deep merges the value with existing client data.</summary>
    public MergeProp DeepMerge()
    {
        _merge.DeepMerge();
        return this;
    }

    /// <summary>Appends at the root (the default).</summary>
    public MergeProp Append()
    {
        _merge.AppendAtRoot();
        return this;
    }

    /// <summary>
    /// Appends only the array at <paramref name="path"/> (relative to the prop) and replaces the rest of the value,
    /// optionally matching items on a field. Emits <c>mergeProps: ["prop.path"]</c>.
    /// </summary>
    public MergeProp Append(string path, string? matchOn = null)
    {
        _merge.AppendAt(path, matchOn);
        return this;
    }

    /// <summary>Appends the arrays at each of <paramref name="paths"/>.</summary>
    public MergeProp Append(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) _merge.AppendAt(path, null);
        return this;
    }

    /// <summary>Appends the arrays at each path, matching items on the given field (value may be null).</summary>
    public MergeProp Append(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) _merge.AppendAt(entry.Key, entry.Value);
        return this;
    }

    /// <summary>Prepends at the root.</summary>
    public MergeProp Prepend()
    {
        _merge.PrependAtRoot();
        return this;
    }

    /// <summary>Prepends only the array at <paramref name="path"/> (relative to the prop), optionally matching items on a field.</summary>
    public MergeProp Prepend(string path, string? matchOn = null)
    {
        _merge.PrependAt(path, matchOn);
        return this;
    }

    /// <summary>Prepends the arrays at each of <paramref name="paths"/>.</summary>
    public MergeProp Prepend(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) _merge.PrependAt(path, null);
        return this;
    }

    /// <summary>Prepends the arrays at each path, matching items on the given field (value may be null).</summary>
    public MergeProp Prepend(IDictionary<string, string?> pathsToMatchOn)
    {
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) _merge.PrependAt(entry.Key, entry.Value);
        return this;
    }

    /// <summary>
    /// Sets the fields (relative to the prop, e.g. <c>id</c> or <c>messages.id</c>) used to match existing items
    /// when merging. Replaces any match fields set earlier, including the constructor's <c>matchOn</c>.
    /// </summary>
    public MergeProp MatchingOn(params string[] fields)
    {
        _merge.SetMatchOn(fields);
        return this;
    }

    /// <summary>Remembers the value on the client once it has been loaded, so later responses skip it.</summary>
    public MergeProp Once(bool once = true, string? @as = null, TimeSpan? until = null)
    {
        _once.ShouldResolveOnce = once;
        if (@as != null) _once.As(@as);
        if (until.HasValue) _once.Until(until.Value);
        return this;
    }

    /// <summary>Remembers the value under a custom key shared across pages. Implies <see cref="Once"/>.</summary>
    public MergeProp As(string key)
    {
        _once.As(key);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public MergeProp Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>. Implies <see cref="Once"/>.</summary>
    public MergeProp Until(TimeSpan duration)
    {
        _once.Until(duration);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>. Implies <see cref="Once"/>.</summary>
    public MergeProp Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        _once.ShouldResolveOnce = true;
        return this;
    }

    public MergeProp WithScroll(ScrollPropConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ScrollConfig = config;
        return this;
    }

    public MergeProp WithScroll(
        int currentPage,
        int? previousPage = null,
        int? nextPage = null,
        string pageName = "page")
    {
        ScrollConfig = new ScrollPropConfig
        {
            CurrentPage = currentPage,
            PreviousPage = previousPage,
            NextPage = nextPage,
            PageName = pageName
        };

        return this;
    }

    static Func<Task<object>> WrapSync(Func<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return () => Task.FromResult(callback());
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();

    // A legacy WithScroll prop lets the infinite-scroll merge-intent header flip root append/prepend.
    IMergeableProp IMergeIntentAware.ForMergeIntent(string? mergeIntent)
    {
        if (ScrollConfig == null || _merge.ShouldDeepMerge || _merge.AppendPaths.Count > 0 || _merge.PrependPaths.Count > 0)
            return this;

        var prepend = mergeIntent?.Trim().ToLowerInvariant() switch
        {
            "prepend" => true,
            "append" => false,
            _ => _merge.PrependsAtRoot
        };

        return new MergeShape(
            ShouldMerge: true,
            ShouldDeepMerge: false,
            AppendsAtRoot: !prepend,
            PrependsAtRoot: prepend,
            AppendPaths: Array.Empty<string>(),
            PrependPaths: Array.Empty<string>(),
            MatchOnPaths: _merge.MatchOnPaths);
    }

    object? IScrollMetadataProvider.GetScrollMetadata(object? resolvedValue, bool reset) => ScrollConfig;

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
}
