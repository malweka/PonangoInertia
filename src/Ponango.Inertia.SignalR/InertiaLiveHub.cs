using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia.SignalR;

/// <summary>
/// The hub the <c>@ponango/inertia-signalr</c> client transport connects to. Clients call
/// <see cref="Subscribe"/> and <see cref="Unsubscribe"/> for the channels their page's <c>liveProps</c> list, and
/// <see cref="IInertiaBroadcaster"/> sends events to the channel's group. Map it with <c>MapInertiaLive()</c>.
/// </summary>
public class InertiaLiveHub : Hub
{
    private readonly InertiaLiveOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<InertiaLiveHub> _logger;

    public InertiaLiveHub(IOptions<InertiaLiveOptions> options, IServiceProvider services, ILogger<InertiaLiveHub> logger)
    {
        _options = options.Value;
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Joins the channel's group. Public channels are open; private and presence channels need a matching rule that
    /// allows the user. Encrypted private channels are not supported over SignalR.
    /// </summary>
    /// <exception cref="HubException">The channel is invalid, unsupported, or the user is not allowed.</exception>
    public async Task Subscribe(string type, string name)
    {
        var channel = ParseChannel(type, name);

        if (channel.Type == LiveChannelType.EncryptedPrivate)
            throw new HubException("Encrypted private channels are not supported over SignalR.");

        if (channel.Type != LiveChannelType.Public && !await AuthorizeAsync(channel))
        {
            _logger.LogDebug("Connection {ConnectionId} was refused the live channel {Channel}.", Context.ConnectionId, channel.Key);
            throw new HubException($"Unauthorized to subscribe to the live channel \"{channel.Name}\".");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, InertiaLiveProtocol.GroupName(channel), Context.ConnectionAborted);
    }

    /// <summary>Leaves the channel's group. Unknown or invalid channels are ignored.</summary>
    public async Task Unsubscribe(string type, string name)
    {
        if (!LiveChannel.TryParse(type, name, out var channel))
            return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, InertiaLiveProtocol.GroupName(channel), Context.ConnectionAborted);
    }

    async Task<bool> AuthorizeAsync(LiveChannel channel)
    {
        var rule = _options.FindRule(channel.Name, out var values);
        if (rule == null)
            return false;

        var context = new LiveChannelAuthorizationContext(
            channel,
            values,
            Context.User ?? new ClaimsPrincipal(),
            Context.GetHttpContext(),
            Context.ConnectionId,
            _services);

        return await rule.Authorize(context);
    }

    /// <summary>
    /// The name comes from the page object, where it is already without its prefix, so it is kept as sent: the same
    /// channel the server broadcasts on.
    /// </summary>
    static LiveChannel ParseChannel(string type, string name)
    {
        if (!LiveChannel.TryParseType(type, out _))
            throw new HubException($"Unknown live channel type \"{type}\".");

        if (!LiveChannel.TryParse(type, name, out var channel))
            throw new HubException("The live channel name is empty.");

        return channel;
    }
}
