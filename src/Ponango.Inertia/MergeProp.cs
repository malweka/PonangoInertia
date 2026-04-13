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
/// instead of replacing it. Used for infinite scroll, real-time feeds, etc.
/// </summary>
public class MergeProp
{
    private readonly Func<Task<object>> _callback;

    /// <summary>
    /// How the client should merge this prop's data.
    /// </summary>
    public MergeMode Mode { get; }

    /// <summary>
    /// Optional key used for matching items during merge (e.g., "id").
    /// Serialized into the matchPropsOn array as "propKey.matchOn".
    /// </summary>
    public string? MatchOn { get; }

    /// <summary>
    /// Optional pagination metadata for infinite scroll responses.
    /// When present, InertiaResult emits a scrollProps entry for this prop key.
    /// </summary>
    public ScrollPropConfig? ScrollConfig { get; private set; }

    public MergeProp(Func<object> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = () => Task.FromResult(callback());
        Mode = mode;
        MatchOn = matchOn;
    }

    public MergeProp(Func<Task<object>> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        Mode = mode;
        MatchOn = matchOn;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

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
}
