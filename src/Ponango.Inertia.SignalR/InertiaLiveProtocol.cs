namespace Ponango.Inertia.SignalR;

/// <summary>
/// The names shared by the hub and the <c>@ponango/inertia-signalr</c> client transport. Change them in both packages
/// together.
/// </summary>
public static class InertiaLiveProtocol
{
    /// <summary>The default path of the hub, used by <c>MapInertiaLive()</c>.</summary>
    public const string DefaultPath = "/inertia/live";

    /// <summary>
    /// The client method every live event is sent to, with two arguments: the channel key (<c>{type}:{name}</c>) and
    /// the <c>{ "__inertia": ... }</c> payload.
    /// </summary>
    public const string ClientMethod = "inertia";

    /// <summary>The hub method a client calls to join a channel: <c>Subscribe(type, name)</c>.</summary>
    public const string SubscribeMethod = "Subscribe";

    /// <summary>The hub method a client calls to leave a channel: <c>Unsubscribe(type, name)</c>.</summary>
    public const string UnsubscribeMethod = "Unsubscribe";

    /// <summary>The SignalR group of a channel: its key, <c>{type}:{name}</c>.</summary>
    public static string GroupName(LiveChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.Key;
    }
}
