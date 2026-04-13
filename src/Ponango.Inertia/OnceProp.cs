namespace Ponango.Inertia;

/// <summary>
/// A prop that is resolved on the first visit and cached by the client.
/// On subsequent visits, the client sends the X-Inertia-Except-Once-Props header
/// with already-loaded keys, and the server skips resolving them.
/// Optionally supports an expiration time after which the client re-fetches.
/// </summary>
public class OnceProp
{
    private readonly Func<Task<object>> _callback;

    /// <summary>
    /// Optional duration after which the client should re-fetch this prop.
    /// When null, the prop never expires and is only fetched once.
    /// </summary>
    public TimeSpan? ExpiresAfter { get; }

    public OnceProp(Func<object> callback, TimeSpan? expiresAfter = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = () => Task.FromResult(callback());
        ExpiresAfter = expiresAfter;
    }

    public OnceProp(Func<Task<object>> callback, TimeSpan? expiresAfter = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        ExpiresAfter = expiresAfter;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();
}
