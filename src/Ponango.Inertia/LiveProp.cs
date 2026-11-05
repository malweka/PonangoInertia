namespace Ponango.Inertia;

/// <summary>
/// A regular prop that the client refreshes when one of its broadcast events arrives: it reloads the prop with a
/// partial reload, or takes the new value from the event payload. Created by <c>Inertia.Live(...)</c>; call
/// <c>Live(...)</c> again to listen for more events. The value may be a delegate without parameters, which is only
/// evaluated when the prop is included.
/// </summary>
public class LiveProp : LiveModifiers<LiveProp>, IResolvableProp
{
    private readonly object? _value;

    internal LiveProp(object? value) => _value = value;

    async Task<object?> IResolvableProp.ResolveAsync()
        => PropsResolver.IsLazy(_value) ? await PropsResolver.InvokeLazyAsync(_value!) : _value;
}
