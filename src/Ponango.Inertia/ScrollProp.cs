namespace Ponango.Inertia;

/// <summary>
/// A paginated prop for Inertia's <c>&lt;InfiniteScroll&gt;</c> component. The array inside the
/// <see cref="Wrapper"/> key (default <c>data</c>) is appended, or prepended when the client sends
/// <c>X-Inertia-Infinite-Scroll-Merge-Intent: prepend</c>, and the pagination state is emitted in
/// <c>scrollProps</c>.
/// </summary>
public class ScrollProp : IResolvableProp, IMergeableProp, IDeferrableProp, IMergeIntentAware, IScrollMetadataProvider
{
    private readonly Func<Task<object>> _callback;
    private readonly Func<object?, ScrollMetadata> _metadata;
    private readonly List<string> _matchOn = new();
    private bool _defer;
    private string _group = "default";

    public ScrollProp(Func<object> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")
        : this(WrapSync(value), metadata, wrapper)
    {
    }

    public ScrollProp(Func<Task<object>> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapper);
        _callback = value;
        _metadata = resolved => metadata(resolved!);
        Wrapper = wrapper;
    }

    public ScrollProp(Func<object> value, ScrollMetadata metadata, string wrapper = "data")
        : this(WrapSync(value), metadata, wrapper)
    {
    }

    public ScrollProp(Func<Task<object>> value, ScrollMetadata metadata, string wrapper = "data")
        : this(value, _ => metadata ?? throw new ArgumentNullException(nameof(metadata)), wrapper)
    {
        ArgumentNullException.ThrowIfNull(metadata);
    }

    /// <summary>The key, inside the prop value, of the array the client merges (default <c>data</c>).</summary>
    public string Wrapper { get; }

    /// <summary>Runs the value callback.</summary>
    public Task<object> InvokeAsync() => _callback();

    /// <summary>
    /// Loads the prop in a follow-up request instead of the initial visit. The full visit then announces it in
    /// <c>deferredProps</c> and emits no <c>scrollProps</c> for it.
    /// </summary>
    public ScrollProp Defer(string group = "default")
    {
        _defer = true;
        _group = string.IsNullOrWhiteSpace(group) ? "default" : group;
        return this;
    }

    /// <summary>
    /// Sets the fields, relative to the wrapped array's items (e.g. <c>id</c>), used to match existing items so
    /// they are updated in place instead of duplicated. Emits <c>matchPropsOn: ["prop.data.id"]</c>.
    /// </summary>
    public ScrollProp MatchingOn(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _matchOn.Clear();
        _matchOn.AddRange(fields.Where(field => !string.IsNullOrWhiteSpace(field)).Select(field => $"{Wrapper}.{field}"));
        return this;
    }

    static Func<Task<object>> WrapSync(Func<object> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return () => Task.FromResult(value());
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await InvokeAsync();

    IMergeableProp IMergeIntentAware.ForMergeIntent(string? mergeIntent)
    {
        var prepend = string.Equals(mergeIntent?.Trim(), "prepend", StringComparison.OrdinalIgnoreCase);
        var paths = new[] { Wrapper };

        return new MergeShape(
            ShouldMerge: true,
            ShouldDeepMerge: false,
            AppendsAtRoot: false,
            PrependsAtRoot: false,
            AppendPaths: prepend ? Array.Empty<string>() : paths,
            PrependPaths: prepend ? paths : Array.Empty<string>(),
            MatchOnPaths: _matchOn);
    }

    object? IScrollMetadataProvider.GetScrollMetadata(object? resolvedValue, bool reset)
        => _metadata(resolvedValue).ToDictionary(reset);

    bool IDeferrableProp.ShouldDefer => _defer;
    string IDeferrableProp.Group => _group;

    bool IMergeableProp.ShouldMerge => true;
    bool IMergeableProp.ShouldDeepMerge => false;
    bool IMergeableProp.AppendsAtRoot => false;
    bool IMergeableProp.PrependsAtRoot => false;
    IReadOnlyList<string> IMergeableProp.AppendPaths => new[] { Wrapper };
    IReadOnlyList<string> IMergeableProp.PrependPaths => Array.Empty<string>();
    IReadOnlyList<string> IMergeableProp.MatchOnPaths => _matchOn;
}
