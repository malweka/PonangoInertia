namespace Ponango.Inertia;

/// <summary>
/// The kind of broadcast channel a live prop listens on. The client transport subscribes differently to each, and a
/// server transport authorizes <see cref="Private"/> and <see cref="Presence"/> channels before subscribing.
/// </summary>
public enum LiveChannelType
{
    /// <summary>Anyone can subscribe.</summary>
    Public,

    /// <summary>The server authorizes the user before subscribing.</summary>
    Private,

    /// <summary>A private channel that also tracks who is connected.</summary>
    Presence,

    /// <summary>A private channel whose payloads are end-to-end encrypted (a Pusher feature).</summary>
    EncryptedPrivate
}

/// <summary>
/// A broadcast channel a live prop listens on: a name such as <c>orders.1</c> and a <see cref="LiveChannelType"/>.
/// A string converts to a public channel, as in the reference adapter.
/// </summary>
public sealed record LiveChannel
{
    /// <summary>
    /// Creates a channel. A Laravel/Pusher-style prefix matching the type (<c>private-</c>, <c>presence-</c>,
    /// <c>private-encrypted-</c>) is removed from the name, since the type is sent separately.
    /// </summary>
    public LiveChannel(string name, LiveChannelType type = LiveChannelType.Public)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var prefix = Prefix(type);
        if (prefix.Length > 0 && name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
            name = name[prefix.Length..];

        Name = name;
        Type = type;
    }

    /// <summary>The channel name, without a type prefix.</summary>
    public string Name { get; }

    /// <summary>The channel type.</summary>
    public LiveChannelType Type { get; }

    /// <summary>The type as the protocol writes it: <c>public</c>, <c>private</c>, <c>presence</c> or <c>encrypted-private</c>.</summary>
    public string TypeName => Type switch
    {
        LiveChannelType.Private => "private",
        LiveChannelType.Presence => "presence",
        LiveChannelType.EncryptedPrivate => "encrypted-private",
        _ => "public"
    };

    /// <summary>The channel's identity, <c>{type}:{name}</c>, as the client keys its subscriptions.</summary>
    public string Key => $"{TypeName}:{Name}";

    /// <summary>A public channel.</summary>
    public static LiveChannel Public(string name) => new(name, LiveChannelType.Public);

    /// <summary>A private channel.</summary>
    public static LiveChannel Private(string name) => new(name, LiveChannelType.Private);

    /// <summary>A presence channel.</summary>
    public static LiveChannel Presence(string name) => new(name, LiveChannelType.Presence);

    /// <summary>An encrypted private channel.</summary>
    public static LiveChannel EncryptedPrivate(string name) => new(name, LiveChannelType.EncryptedPrivate);

    /// <summary>Parses a protocol type name (<see cref="TypeName"/>). Returns false for an unknown name.</summary>
    public static bool TryParseType(string? typeName, out LiveChannelType type)
    {
        switch (typeName)
        {
            case "public": type = LiveChannelType.Public; return true;
            case "private": type = LiveChannelType.Private; return true;
            case "presence": type = LiveChannelType.Presence; return true;
            case "encrypted-private": type = LiveChannelType.EncryptedPrivate; return true;
            default: type = default; return false;
        }
    }

    /// <summary>A bare string is a public channel.</summary>
    public static implicit operator LiveChannel(string name) => new(name);

    /// <inheritdoc />
    public override string ToString() => Key;

    static string Prefix(LiveChannelType type) => type switch
    {
        LiveChannelType.Private => "private-",
        LiveChannelType.Presence => "presence-",
        LiveChannelType.EncryptedPrivate => "private-encrypted-",
        _ => ""
    };
}

/// <summary>
/// A broadcast event that names itself and the channels it is sent on, the .NET counterpart of a Laravel event's
/// <c>broadcastAs()</c> and <c>broadcastOn()</c>. Pass it to <c>Live(...)</c> to listen for it, and use the same
/// type when broadcasting, so the listener and the sender cannot drift apart.
/// </summary>
public interface ILiveEvent
{
    /// <summary>The event name, such as <c>order.updated</c>.</summary>
    string EventName { get; }

    /// <summary>The channels the event is broadcast on.</summary>
    IEnumerable<LiveChannel> Channels { get; }
}
