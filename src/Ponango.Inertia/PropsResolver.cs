using Microsoft.Extensions.Logging;

namespace Ponango.Inertia;

/// <summary>
/// Decides which props a response includes, resolves them, and collects the page-object metadata that
/// describes them (deferred, merge, once, scroll, rescued). Mirrors the reference adapter's
/// <c>PropsResolver</c> (inertia-laravel 3.x).
/// </summary>
internal sealed class PropsResolver
{
    private readonly bool _isInertia;
    private readonly bool _isPartial;
    private readonly HashSet<string>? _only;
    private readonly HashSet<string>? _except;
    private readonly HashSet<string> _resetProps;
    private readonly HashSet<string> _loadedOnceProps;
    private readonly string? _mergeIntent;
    private readonly ILogger _logger;

    public PropsResolver(InertiaRequestHeaders headers, bool isInertia, string component, ILogger logger)
    {
        _isInertia = isInertia;
        _isPartial = isInertia &&
                     !string.IsNullOrEmpty(headers.PartialComponent) &&
                     string.Equals(headers.PartialComponent, component, StringComparison.Ordinal);
        _only = ParseHeader(headers.PartialData);
        _except = ParseHeader(headers.PartialExcept);
        _resetProps = ParseHeader(headers.Reset) ?? new HashSet<string>(StringComparer.Ordinal);
        _loadedOnceProps = ParseHeader(headers.ExceptOnceProps) ?? new HashSet<string>(StringComparer.Ordinal);
        _mergeIntent = headers.MergeIntent;
        _logger = logger;
    }

    public bool IsPartial => _isPartial;

    public Dictionary<string, List<string>> DeferredProps { get; } = new(StringComparer.Ordinal);
    public List<string> RescuedProps { get; } = new();
    public List<string> MergeProps { get; } = new();
    public List<string> PrependProps { get; } = new();
    public List<string> DeepMergeProps { get; } = new();
    public List<string> MatchPropsOn { get; } = new();
    public Dictionary<string, object> ScrollProps { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object> OnceProps { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the given props, in order, and returns the ones the response should include.
    /// </summary>
    public async Task<Dictionary<string, object?>> ResolveAsync(IEnumerable<KeyValuePair<string, object?>> props)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, original) in props)
        {
            var path = key;
            var prop = original;

            // On partial requests only the requested paths are included. Always props and errors bypass this.
            if (!ShouldIncludeInPartialResponse(prop, path))
                continue;

            // On full visits some prop types are excluded before resolution, so their callbacks never run.
            if (!_isPartial && ExcludeFromInitialResponse(prop, path))
                continue;

            var (resolved, value) = await TryResolveValueAsync(prop, path);
            if (!resolved)
                continue;

            // A callback may return another prop type; unwrap it one more level so it can take part in
            // exclusion and metadata collection.
            if (!ReferenceEquals(value, prop) && value is IResolvableProp)
            {
                prop = value;

                if (!_isPartial && ExcludeFromInitialResponse(prop, path))
                    continue;

                (resolved, value) = await TryResolveValueAsync(prop, path);
                if (!resolved)
                    continue;
            }

            CollectMetadata(prop, path, value);
            result[key] = value;
        }

        return result;
    }

    bool ShouldIncludeInPartialResponse(object? prop, string path)
    {
        if (!_isPartial || prop is AlwaysProp || path == "errors")
            return true;

        return PathMatchesPartialRequest(path);
    }

    bool PathMatchesPartialRequest(string path)
    {
        if (_only != null && !MatchesOnly(path) && !LeadsToOnly(path))
            return false;

        if (_except != null && MatchesExcept(path))
            return false;

        return true;
    }

    bool ExcludeFromInitialResponse(object? prop, string path)
    {
        // Optional and deferred props are never sent on a full visit, but still contribute their
        // deferred, merge, and once metadata.
        if (prop is IIgnoreFirstLoad)
        {
            if (prop is IDeferrableProp { ShouldDefer: true } deferrable && !WasAlreadyLoadedByClient(prop, path))
                AddDeferred(deferrable.Group, path);

            if (prop is IMergeableProp { ShouldMerge: true } mergeable)
                CollectMergeable(path, mergeable);

            if (prop is IOnceableProp { ShouldResolveOnce: true } onceable)
                CollectOnce(path, onceable);

            return true;
        }

        // Other deferrable types (e.g. a deferred scroll prop) announce their group and merge behavior only.
        if (prop is IDeferrableProp { ShouldDefer: true } deferred)
        {
            AddDeferred(deferred.Group, path);

            if (prop is IMergeableProp { ShouldMerge: true } mergeable)
                CollectMergeable(path, mergeable);

            return true;
        }

        // Once props the client already holds are skipped, but keep their metadata so the client keeps them.
        if (_isInertia && WasAlreadyLoadedByClient(prop, path))
        {
            CollectOnce(path, (IOnceableProp)prop!);
            return true;
        }

        return false;
    }

    bool WasAlreadyLoadedByClient(object? prop, string path)
        => prop is IOnceableProp { ShouldResolveOnce: true, ShouldBeRefreshed: false } onceable
           && _loadedOnceProps.Contains(onceable.OnceKey ?? path);

    async Task<(bool Resolved, object? Value)> TryResolveValueAsync(object? prop, string path)
    {
        // A plain delegate is a lazily evaluated regular prop: it only runs when the prop is included.
        if (prop is Func<object> lazy)
            return (true, await UnwrapTaskAsync(lazy()));

        if (prop is not IResolvableProp resolvable)
            return (true, prop);

        try
        {
            return (true, await resolvable.ResolveAsync());
        }
        catch (Exception exception) when (prop is IRescuableProp { ShouldRescue: true })
        {
            _logger.LogError(exception, "Inertia prop '{Prop}' failed to resolve and was rescued.", path);
            RescuedProps.Add(path);
            return (false, null);
        }
    }

    // Func<T> is covariant, so a Func<Task<T>> also arrives here as a Func<object> returning a task.
    static async Task<object?> UnwrapTaskAsync(object? value)
    {
        if (value is not Task task)
            return value;

        await task;

        var type = task.GetType();
        while (type != null && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)))
            type = type.BaseType;

        // A non-generic Task (internally Task<VoidTaskResult>) has no meaningful result.
        if (type == null || type.GetGenericArguments()[0].Name == "VoidTaskResult")
            return null;

        return type.GetProperty(nameof(Task<object>.Result))!.GetValue(task);
    }

    void CollectMetadata(object? prop, string path, object? resolvedValue)
    {
        if (prop is IMergeableProp { ShouldMerge: true } mergeable)
            CollectMergeable(path, mergeable);

        if (prop is IScrollMetadataProvider scroll &&
            scroll.GetScrollMetadata(resolvedValue, _resetProps.Contains(path)) is { } metadata)
            ScrollProps[path] = metadata;

        if (prop is IOnceableProp { ShouldResolveOnce: true } onceable)
            CollectOnce(path, onceable);
    }

    void AddDeferred(string group, string path)
    {
        if (!DeferredProps.TryGetValue(group, out var keys))
        {
            keys = new List<string>();
            DeferredProps[group] = keys;
        }

        keys.Add(path);
    }

    void CollectMergeable(string path, IMergeableProp prop)
    {
        // A reset prop is returned unlabeled so the client replaces it instead of merging.
        if (_resetProps.Contains(path))
            return;

        if (_isPartial && !IsIncludedInPartialMetadata(path))
            return;

        if (prop is IMergeIntentAware intentAware)
            prop = intentAware.ForMergeIntent(_mergeIntent);

        if (prop.ShouldDeepMerge)
        {
            DeepMergeProps.Add(path);
        }
        else if (prop.AppendsAtRoot)
        {
            MergeProps.Add(path);
        }
        else if (prop.PrependsAtRoot)
        {
            PrependProps.Add(path);
        }
        else
        {
            foreach (var appendPath in prop.AppendPaths)
                MergeProps.Add($"{path}.{appendPath}");

            foreach (var prependPath in prop.PrependPaths)
                PrependProps.Add($"{path}.{prependPath}");
        }

        foreach (var matchOn in prop.MatchOnPaths)
            MatchPropsOn.Add($"{path}.{matchOn}");
    }

    void CollectOnce(string path, IOnceableProp prop)
    {
        if (_isPartial && !IsIncludedInPartialMetadata(path))
            return;

        // A dictionary (not an object) so a null expiresAt is still serialized.
        OnceProps[prop.OnceKey ?? path] = new Dictionary<string, object?>
        {
            ["prop"] = path,
            ["expiresAt"] = prop.ExpiresAtMilliseconds()
        };
    }

    bool IsIncludedInPartialMetadata(string path)
    {
        if (_only != null && !MatchesOnly(path))
            return false;

        if (_except != null && MatchesExcept(path))
            return false;

        return true;
    }

    // The path matches, or is a descendant of, an "only" path.
    bool MatchesOnly(string path)
        => _only!.Any(only => path == only || path.StartsWith(only + ".", StringComparison.Ordinal));

    // The path is an ancestor of an "only" path.
    bool LeadsToOnly(string path)
        => _only!.Any(only => only.StartsWith(path + ".", StringComparison.Ordinal));

    // The path matches, or is a descendant of, an "except" path.
    bool MatchesExcept(string path)
        => _except!.Any(except => path == except || path.StartsWith(except + ".", StringComparison.Ordinal));

    internal static HashSet<string>? ParseHeader(string? header)
    {
        if (string.IsNullOrEmpty(header))
            return null;

        var values = header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return values.Count > 0 ? values : null;
    }
}
