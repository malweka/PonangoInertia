namespace Ponango.Inertia;

/// <summary>
/// A prop that is excluded from the initial page response and loaded by the client
/// in a follow-up partial reload request. Deferred props can be grouped so that
/// multiple props in the same group are fetched in a single request.
/// </summary>
public class DeferredProp
{
    private readonly Func<Task<object>> _callback;

    /// <summary>
    /// The group name for this deferred prop. Props in the same group are fetched together.
    /// Defaults to "default".
    /// </summary>
    public string Group { get; }

    public DeferredProp(Func<object> callback, string group = "default")
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = () => Task.FromResult(callback());
        Group = group;
    }

    public DeferredProp(Func<Task<object>> callback, string group = "default")
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        Group = group;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();
}
