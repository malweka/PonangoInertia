using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ponango.Inertia.SignalR;

/// <summary>
/// Sends live events to the clients subscribed to their channels through <see cref="InertiaLiveHub"/>. Without
/// props, clients reload the live props that listen for the event; with props, they take the values from the event.
/// </summary>
public interface IInertiaBroadcaster
{
    /// <summary>
    /// Sends <paramref name="event"/> on each of its channels. <paramref name="props"/> (an anonymous object or a
    /// dictionary keyed by prop path) is resolved like page props, see <c>Inertia.BroadcastPropsAsync</c>; only send
    /// values that are the same for every subscriber. <paramref name="exceptSocketId"/> skips that connection, usually
    /// the request's <c>InertiaContext.SocketId</c>.
    /// </summary>
    Task BroadcastAsync(ILiveEvent @event, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default);

    /// <summary>Sends the event named <paramref name="event"/> on <paramref name="channel"/>.</summary>
    Task BroadcastAsync(string @event, LiveChannel channel, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default);

    /// <summary>Sends the event named <paramref name="event"/> on each of <paramref name="channels"/>.</summary>
    Task BroadcastAsync(string @event, IEnumerable<LiveChannel> channels, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default);
}

internal sealed class InertiaBroadcaster : IInertiaBroadcaster
{
    private readonly IHubContext<InertiaLiveHub> _hub;
    private readonly ILogger<InertiaBroadcaster> _logger;

    public InertiaBroadcaster(IHubContext<InertiaLiveHub> hub, ILogger<InertiaBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task BroadcastAsync(ILiveEvent @event, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return BroadcastAsync(@event.EventName, @event.Channels, props, exceptSocketId, cancellationToken);
    }

    public Task BroadcastAsync(string @event, LiveChannel channel, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return BroadcastAsync(@event, new[] { channel }, props, exceptSocketId, cancellationToken);
    }

    public async Task BroadcastAsync(string @event, IEnumerable<LiveChannel> channels, object? props = null, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentNullException.ThrowIfNull(channels);

        var targets = channels.ToList();
        if (targets.Any(channel => channel == null))
            throw new ArgumentException("Live channels cannot be null.", nameof(channels));

        if (targets.FirstOrDefault(channel => channel.Type == LiveChannelType.EncryptedPrivate) is { } encrypted)
            throw new NotSupportedException($"Encrypted private channels are not supported over SignalR ({encrypted.Name}).");

        // SignalR excludes the sender itself, so the payload carries no socket id.
        var payload = await Inertia.BroadcastPropsAsync(
            props ?? new Dictionary<string, object?>(),
            @event,
            socketId: null,
            _logger);

        foreach (var group in targets.Select(InertiaLiveProtocol.GroupName).Distinct(StringComparer.Ordinal))
        {
            var clients = string.IsNullOrEmpty(exceptSocketId)
                ? _hub.Clients.Group(group)
                : _hub.Clients.GroupExcept(group, exceptSocketId);

            await clients.SendAsync(InertiaLiveProtocol.ClientMethod, group, payload, cancellationToken);
        }
    }
}
