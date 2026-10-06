namespace Ponango.Inertia;

/// <summary>
/// A prop that is always included in responses, even during partial reloads
/// when it is not explicitly requested. Use this for data that must always
/// be fresh on every response (e.g., auth state).
/// </summary>
public class AlwaysProp : IResolvableProp
{
    private readonly Func<Task<object>> _callback;

    public AlwaysProp(Func<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = () => Task.FromResult(callback());
    }

    public AlwaysProp(Func<Task<object>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();
}
