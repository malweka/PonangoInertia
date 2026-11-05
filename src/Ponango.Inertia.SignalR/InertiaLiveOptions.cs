using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Ponango.Inertia.SignalR;

/// <summary>
/// Configures <see cref="InertiaLiveHub"/>: which users may subscribe to which private and presence channels.
/// Public channels are open to every connection, as in Laravel broadcasting.
/// </summary>
public sealed class InertiaLiveOptions
{
    private readonly List<ChannelRule> _rules = new();

    /// <summary>
    /// Authorizes private and presence channels whose name matches <paramref name="pattern"/>. Segments are separated
    /// by dots and <c>{name}</c> captures a segment, so <c>orders.{id}</c> matches <c>orders.1</c> and passes
    /// <c>Values["id"] == "1"</c>. The first matching rule decides; a private or presence channel without a matching
    /// rule is refused.
    /// </summary>
    public InertiaLiveOptions Channel(string pattern, Func<LiveChannelAuthorizationContext, bool> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        return Channel(pattern, context => Task.FromResult(authorize(context)));
    }

    /// <inheritdoc cref="Channel(string, Func{LiveChannelAuthorizationContext, bool})"/>
    public InertiaLiveOptions Channel(string pattern, Func<LiveChannelAuthorizationContext, Task<bool>> authorize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(authorize);
        _rules.Add(new ChannelRule(ChannelPattern.Parse(pattern), authorize));
        return this;
    }

    internal ChannelRule? FindRule(string channelName, out IReadOnlyDictionary<string, string> values)
    {
        foreach (var rule in _rules)
        {
            if (rule.Pattern.TryMatch(channelName, out values))
                return rule;
        }

        values = new Dictionary<string, string>();
        return null;
    }

    internal sealed record ChannelRule(ChannelPattern Pattern, Func<LiveChannelAuthorizationContext, Task<bool>> Authorize);
}

/// <summary>What a channel rule gets to decide whether a connection may subscribe.</summary>
public sealed class LiveChannelAuthorizationContext
{
    internal LiveChannelAuthorizationContext(
        LiveChannel channel,
        IReadOnlyDictionary<string, string> values,
        ClaimsPrincipal user,
        HttpContext? httpContext,
        string connectionId,
        IServiceProvider services)
    {
        Channel = channel;
        Values = values;
        User = user;
        HttpContext = httpContext;
        ConnectionId = connectionId;
        Services = services;
    }

    /// <summary>The channel being subscribed to.</summary>
    public LiveChannel Channel { get; }

    /// <summary>The segments captured by the rule's pattern, such as <c>id</c> for <c>orders.{id}</c>.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>The connection's user (an unauthenticated principal when nobody is signed in).</summary>
    public ClaimsPrincipal User { get; }

    /// <summary>The HTTP context of the connection, when the transport has one.</summary>
    public HttpContext? HttpContext { get; }

    /// <summary>The SignalR connection id, which the client also sends as <c>X-Socket-Id</c>.</summary>
    public string ConnectionId { get; }

    /// <summary>Services scoped to the hub invocation, for checks that need a database.</summary>
    public IServiceProvider Services { get; }
}

/// <summary>A dot-separated channel name pattern with <c>{name}</c> captures.</summary>
internal sealed class ChannelPattern
{
    private readonly string[] _segments;

    private ChannelPattern(string[] segments) => _segments = segments;

    public static ChannelPattern Parse(string pattern)
    {
        var segments = pattern.Split('.');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                throw new ArgumentException($"The channel pattern [{pattern}] has an empty segment.", nameof(pattern));

            if (IsCapture(segment) && segment.Length == 2)
                throw new ArgumentException($"The channel pattern [{pattern}] has a capture without a name.", nameof(pattern));
        }

        return new ChannelPattern(segments);
    }

    public bool TryMatch(string channelName, out IReadOnlyDictionary<string, string> values)
    {
        var parts = channelName.Split('.');
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        values = captured;

        if (parts.Length != _segments.Length)
            return false;

        for (var i = 0; i < parts.Length; i++)
        {
            var segment = _segments[i];
            if (IsCapture(segment))
            {
                if (parts[i].Length == 0)
                    return false;

                captured[segment[1..^1]] = parts[i];
            }
            else if (!string.Equals(segment, parts[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    static bool IsCapture(string segment) => segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}';
}
