namespace Ponango.Inertia;

/// <summary>
/// A prop that is only included in responses when explicitly requested by a partial reload
/// (<c>only</c>/<c>except</c>). Excluded from full visits and never announced.
/// This is the v3 replacement for LazyProp.
/// </summary>
public class OptionalProp : IResolvableProp, IIgnoreFirstLoad, IOnceableProp
{
    private readonly Func<Task<object>> _callback;
    private readonly OnceBehavior _once = new();

    public OptionalProp(Func<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = () => Task.FromResult(callback());
    }

    public OptionalProp(Func<Task<object>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>
    /// Remembers the value on the client once it has been loaded, so later responses skip it.
    /// </summary>
    public OptionalProp Once(bool once = true, string? @as = null, TimeSpan? until = null)
    {
        _once.ShouldResolveOnce = once;
        if (@as != null) _once.As(@as);
        if (until.HasValue) _once.Until(until.Value);
        return this;
    }

    /// <summary>Remembers the value under a custom key shared across pages. Implies <see cref="Once"/>.</summary>
    public OptionalProp As(string key)
    {
        _once.As(key);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public OptionalProp Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>. Implies <see cref="Once"/>.</summary>
    public OptionalProp Until(TimeSpan duration)
    {
        _once.Until(duration);
        _once.ShouldResolveOnce = true;
        return this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>. Implies <see cref="Once"/>.</summary>
    public OptionalProp Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        _once.ShouldResolveOnce = true;
        return this;
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();

    bool IOnceableProp.ShouldResolveOnce => _once.ShouldResolveOnce;
    bool IOnceableProp.ShouldBeRefreshed => _once.ShouldBeRefreshed;
    string? IOnceableProp.OnceKey => _once.OnceKey;
    long? IOnceableProp.ExpiresAtMilliseconds() => _once.ExpiresAtMilliseconds();
}
