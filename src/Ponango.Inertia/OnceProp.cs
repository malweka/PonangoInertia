namespace Ponango.Inertia;

/// <summary>
/// A prop that is resolved on the first visit and cached by the client.
/// On subsequent visits, the client sends the X-Inertia-Except-Once-Props header
/// with already-loaded keys, and the server skips resolving them.
/// Optionally supports an expiration time after which the client re-fetches.
/// </summary>
public class OnceProp : IResolvableProp, IOnceableProp
{
    private readonly Func<Task<object>> _callback;
    private readonly OnceBehavior _once = new() { ShouldResolveOnce = true };

    /// <summary>
    /// Optional duration after which the client should re-fetch this prop.
    /// When null, the prop never expires (unless an absolute expiry was set with <see cref="Until(DateTimeOffset)"/>).
    /// </summary>
    public TimeSpan? ExpiresAfter => _once.ExpiresAfter;

    public OnceProp(Func<object> callback, TimeSpan? expiresAfter = null)
        : this(() => Task.FromResult(callback()), expiresAfter)
    {
        ArgumentNullException.ThrowIfNull(callback);
    }

    public OnceProp(Func<Task<object>> callback, TimeSpan? expiresAfter = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        if (expiresAfter.HasValue)
            _once.Until(expiresAfter.Value);
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>
    /// Remembers the value under a custom key, so several pages (or prop names) share one cached value.
    /// </summary>
    public OnceProp As(string key)
    {
        _once.As(key);
        return this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public OnceProp Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>.</summary>
    public OnceProp Until(TimeSpan duration)
    {
        _once.Until(duration);
        return this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>.</summary>
    public OnceProp Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        return this;
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();

    bool IOnceableProp.ShouldResolveOnce => _once.ShouldResolveOnce;
    bool IOnceableProp.ShouldBeRefreshed => _once.ShouldBeRefreshed;
    string? IOnceableProp.OnceKey => _once.OnceKey;
    long? IOnceableProp.ExpiresAtMilliseconds() => _once.ExpiresAtMilliseconds();
}
