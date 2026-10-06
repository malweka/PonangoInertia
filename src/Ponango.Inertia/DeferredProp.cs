namespace Ponango.Inertia;

/// <summary>
/// A prop that is excluded from the initial page response and loaded by the client
/// in a follow-up partial reload request. Deferred props can be grouped so that
/// multiple props in the same group are fetched in a single request.
/// Deferred props can also merge (<see cref="Merge"/>, <c>DeepMerge</c>, <c>Append</c>, <c>Prepend</c>), be
/// remembered by the client (<c>Once</c>; a remembered deferred prop is no longer announced in
/// <c>deferredProps</c>) and rescue resolution errors (<see cref="Rescue"/>).
/// </summary>
public class DeferredProp : MergeModifiers<DeferredProp>, IResolvableProp, IIgnoreFirstLoad, IDeferrableProp, IRescuableProp
{
    private readonly Func<Task<object>> _callback;
    private bool _rescue;

    /// <summary>
    /// The group name for this deferred prop. Props in the same group are fetched together.
    /// Defaults to "default".
    /// </summary>
    public string Group { get; }

    public DeferredProp(Func<object> callback, string group = "default", bool rescue = false)
        : this(WrapSync(callback), group, rescue)
    {
    }

    public DeferredProp(Func<Task<object>> callback, string group = "default", bool rescue = false)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        Group = string.IsNullOrWhiteSpace(group) ? "default" : group;
        _rescue = rescue;
    }

    public Task<object> InvokeAsync() => _callback();

    public object Invoke() => _callback().GetAwaiter().GetResult();

    /// <summary>
    /// When the callback throws, omit the prop, log the exception, and list the key in <c>rescuedProps</c>
    /// so the client can render the <c>rescue</c> slot of its <c>Deferred</c> component.
    /// </summary>
    public DeferredProp Rescue(bool rescue = true)
    {
        _rescue = rescue;
        return this;
    }

    /// <summary>Appends the loaded value to existing client data at the root. Same as <c>Append()</c>.</summary>
    public DeferredProp Merge() => Append();

    static Func<Task<object>> WrapSync(Func<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return () => Task.FromResult(callback());
    }

    async Task<object?> IResolvableProp.ResolveAsync() => await _callback();

    bool IDeferrableProp.ShouldDefer => true;

    bool IRescuableProp.ShouldRescue => _rescue;
}
