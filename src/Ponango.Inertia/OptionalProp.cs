namespace Ponango.Inertia;

/// <summary>
/// A prop that is only included in responses when explicitly requested by a partial reload
/// (<c>only</c>/<c>except</c>). Excluded from full visits and never announced.
/// This is the v3 replacement for LazyProp.
/// </summary>
public class OptionalProp : OnceModifiers<OptionalProp>, IResolvableProp, IIgnoreFirstLoad
{
    private readonly Func<Task<object>> _callback;

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

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();
}
