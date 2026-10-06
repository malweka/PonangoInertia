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
/// Merging can target the root value or nested paths (<c>Append(path)</c>, <c>Prepend(path)</c>).
/// </summary>
public class MergeProp : MergeModifiers<MergeProp>, IResolvableProp, IMergeIntentAware, IScrollMetadataProvider
{
    private readonly Func<Task<object>> _callback;

    /// <summary>
    /// How the client merges this prop's root value.
    /// </summary>
    public MergeMode Mode => MergeState.ShouldDeepMerge
        ? MergeMode.DeepMerge
        : MergeState.PrependsAtRoot ? MergeMode.Prepend : MergeMode.Append;

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
                MergeState.DeepMerge();
                break;
            case MergeMode.Prepend:
                MergeState.PrependAtRoot();
                break;
            default:
                MergeState.AppendAtRoot();
                break;
        }

        if (!string.IsNullOrWhiteSpace(matchOn))
            MergeState.SetMatchOn(new[] { matchOn });
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>
    /// Attaches infinite scroll metadata. The whole prop value is merged at the root.
    /// </summary>
    [Obsolete("Use Inertia.Scroll(...) instead, which merges the inner data array and supports cursors and deferral.")]
    public MergeProp WithScroll(ScrollPropConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ScrollConfig = config;
        return this;
    }

    /// <summary>
    /// Attaches infinite scroll metadata. The whole prop value is merged at the root.
    /// </summary>
    [Obsolete("Use Inertia.Scroll(...) instead, which merges the inner data array and supports cursors and deferral.")]
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
        if (ScrollConfig == null || MergeState.ShouldDeepMerge || MergeState.AppendPaths.Count > 0 || MergeState.PrependPaths.Count > 0)
            return this;

        var prepend = mergeIntent?.Trim().ToLowerInvariant() switch
        {
            "prepend" => true,
            "append" => false,
            _ => MergeState.PrependsAtRoot
        };

        return new MergeShape(
            ShouldMerge: true,
            ShouldDeepMerge: false,
            AppendsAtRoot: !prepend,
            PrependsAtRoot: prepend,
            AppendPaths: Array.Empty<string>(),
            PrependPaths: Array.Empty<string>(),
            MatchOnPaths: MergeState.MatchOnPaths);
    }

    object? IScrollMetadataProvider.GetScrollMetadata(object? resolvedValue, bool reset)
        => ScrollConfig == null
            ? null
            : new ScrollMetadata(ScrollConfig.PageName, ScrollConfig.PreviousPage, ScrollConfig.NextPage, ScrollConfig.CurrentPage)
                .ToDictionary(reset);
}
