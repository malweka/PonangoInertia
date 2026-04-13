namespace Ponango.Inertia;

/// <summary>
/// A prop that is always included in responses, even during partial reloads
/// when it is not explicitly requested. Use this for data that must always
/// be fresh on every response (e.g., auth state, flash messages).
/// </summary>
public class AlwaysProp
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
}
