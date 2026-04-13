namespace Ponango.Inertia;

/// <summary>
/// A prop that is only included in responses when explicitly requested
/// via the X-Inertia-Partial-Data header. Excluded from full visits.
/// This is the v3 replacement for LazyProp.
/// </summary>
public class OptionalProp
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
}
