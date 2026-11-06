namespace Ponango.Inertia;

/// <summary>
/// Merge configuration shared by <see cref="MergeProp"/>, <see cref="DeferredProp"/> and <see cref="ScrollProp"/>.
/// </summary>
internal sealed class MergeBehavior : IMergeableProp
{
    private readonly List<string> _appendPaths = new();
    private readonly List<string> _prependPaths = new();
    private readonly List<string> _matchOn = new();
    private bool _append = true;

    public bool ShouldMerge { get; private set; }
    public bool ShouldDeepMerge { get; private set; }
    public bool AppendsAtRoot => _append && MergesAtRoot;
    public bool PrependsAtRoot => !_append && MergesAtRoot;
    public IReadOnlyList<string> AppendPaths => _appendPaths;
    public IReadOnlyList<string> PrependPaths => _prependPaths;
    public IReadOnlyList<string> MatchOnPaths => _matchOn;

    bool MergesAtRoot => _appendPaths.Count == 0 && _prependPaths.Count == 0;

    public void DeepMerge()
    {
        ShouldDeepMerge = true;
        ShouldMerge = true;
    }

    public void AppendAtRoot()
    {
        _append = true;
        ShouldMerge = true;
    }

    public void PrependAtRoot()
    {
        _append = false;
        ShouldMerge = true;
    }

    public void AppendAt(string path, string? matchOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _appendPaths.Add(path);
        AddPathMatch(path, matchOn);
        ShouldMerge = true;
    }

    public void PrependAt(string path, string? matchOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _prependPaths.Add(path);
        AddPathMatch(path, matchOn);
        ShouldMerge = true;
    }

    public void SetMatchOn(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _matchOn.Clear();
        _matchOn.AddRange(fields.Where(field => !string.IsNullOrWhiteSpace(field)));
    }

    void AddPathMatch(string path, string? matchOn)
    {
        if (!string.IsNullOrWhiteSpace(matchOn))
            _matchOn.Add($"{path}.{matchOn}");
    }
}

/// <summary>
/// An immutable merge shape, used when the shape is computed per request (for example from the merge-intent header).
/// </summary>
internal sealed record MergeShape(
    bool ShouldMerge,
    bool ShouldDeepMerge,
    bool AppendsAtRoot,
    bool PrependsAtRoot,
    IReadOnlyList<string> AppendPaths,
    IReadOnlyList<string> PrependPaths,
    IReadOnlyList<string> MatchOnPaths) : IMergeableProp;

/// <summary>
/// Once configuration shared by <see cref="OnceProp"/>, <see cref="OptionalProp"/>, <see cref="DeferredProp"/>
/// and <see cref="MergeProp"/>.
/// </summary>
internal sealed class OnceBehavior : IOnceableProp
{
    private DateTimeOffset? _until;

    public bool ShouldResolveOnce { get; set; }
    public bool ShouldBeRefreshed { get; set; }
    public string? OnceKey { get; private set; }
    public TimeSpan? ExpiresAfter { get; private set; }

    public void As(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        OnceKey = key;
    }

    public void Until(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "The once-prop lifetime must be positive.");

        ExpiresAfter = duration;
        _until = null;
    }

    public void Until(DateTimeOffset expiresAt)
    {
        _until = expiresAt;
        ExpiresAfter = null;
    }

    // Computed per response, so a relative lifetime starts when the value is sent.
    public long? ExpiresAtMilliseconds()
    {
        if (_until.HasValue)
            return _until.Value.ToUnixTimeMilliseconds();

        if (ExpiresAfter.HasValue)
            return DateTimeOffset.UtcNow.Add(ExpiresAfter.Value).ToUnixTimeMilliseconds();

        return null;
    }
}

/// <summary>
/// Live-update configuration shared by every prop type that supports <c>Live(...)</c>. Each <c>Live</c> call adds a
/// declaration; a channel given with a declaration applies only to that declaration's events. Mirrors the reference
/// adapter's <c>HasLiveUpdates</c> trait.
/// </summary>
internal sealed class LiveBehavior
{
    private readonly List<Declaration> _declarations = new();

    public bool IsLive { get; private set; }

    public TimeSpan? Throttle { get; private set; }

    public void Add(IEnumerable<string> events, IEnumerable<LiveChannel> channels, TimeSpan? throttle)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(channels);

        var names = events.ToList();
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Live event names cannot be empty.", nameof(events));

        AddDeclaration(names.Select(name => new LiveEventSource(name, null)), channels, throttle);
    }

    public void Add(IEnumerable<ILiveEvent> events, IEnumerable<LiveChannel> channels, TimeSpan? throttle)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(channels);

        var list = events.ToList();
        if (list.Any(e => e == null))
            throw new ArgumentException("Live events cannot be null.", nameof(events));

        AddDeclaration(list.Select(e => new LiveEventSource(null, e)), channels, throttle);
    }

    void AddDeclaration(IEnumerable<LiveEventSource> events, IEnumerable<LiveChannel> channels, TimeSpan? throttle)
    {
        if (throttle is { } value && value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(throttle), "The live throttle cannot be negative.");

        var channelList = channels.ToList();
        if (channelList.Any(channel => channel == null))
            throw new ArgumentException("Live channels cannot be null.", nameof(channels));

        _declarations.Add(new Declaration(events.ToList(), channelList));
        IsLive = true;

        if (throttle.HasValue)
            Throttle = throttle;
    }

    /// <summary>The throttle in milliseconds, as the protocol sends it.</summary>
    public long? ThrottleMilliseconds => Throttle.HasValue ? (long)Throttle.Value.TotalMilliseconds : null;

    /// <summary>
    /// The listeners for the page object: grouped by channel (<c>{type}:{name}</c>, first-seen order), each event once
    /// per channel.
    /// </summary>
    /// <exception cref="InvalidOperationException">No event was declared, or an event has no channel.</exception>
    public IReadOnlyList<LiveListener> BuildListeners()
    {
        if (!_declarations.Any(declaration => declaration.Events.Count > 0))
            throw new InvalidOperationException(
                "A live property must listen for at least one event. Pass the event to the [on] argument.");

        var listeners = new List<LiveListener>();
        var byKey = new Dictionary<string, LiveListener>(StringComparer.Ordinal);

        foreach (var declaration in _declarations)
        {
            foreach (var source in declaration.Events)
            {
                var eventName = source.Event?.EventName ?? source.Name!;
                if (string.IsNullOrWhiteSpace(eventName))
                    throw new InvalidOperationException(
                        $"The [{source.Event!.GetType().Name}] live event has an empty event name.");

                var channels = declaration.Channels.Count > 0
                    ? declaration.Channels
                    : ChannelsFromEvent(source);

                foreach (var channel in channels)
                {
                    if (!byKey.TryGetValue(channel.Key, out var listener))
                    {
                        listener = new LiveListener(channel);
                        byKey[channel.Key] = listener;
                        listeners.Add(listener);
                    }

                    listener.AddEvent(eventName);
                }
            }
        }

        return listeners;
    }

    static IReadOnlyList<LiveChannel> ChannelsFromEvent(LiveEventSource source)
    {
        if (source.Event == null)
            throw new InvalidOperationException(
                $"Unable to resolve the channels for the [{source.Name}] event because it was given as a string. " +
                "Pass the [channel] argument, or pass an ILiveEvent to the [on] argument.");

        var channels = source.Event.Channels?.ToList() ?? new List<LiveChannel>();
        if (channels.Count == 0 || channels.Any(channel => channel == null))
            throw new InvalidOperationException(
                $"The [{source.Event.GetType().Name}] live event does not broadcast on any channels. " +
                "Pass the [channel] argument instead.");

        return channels;
    }

    private sealed record LiveEventSource(string? Name, ILiveEvent? Event);

    private sealed record Declaration(IReadOnlyList<LiveEventSource> Events, IReadOnlyList<LiveChannel> Channels);
}

/// <summary>
/// One entry of a live prop's <c>listeners</c>: a channel and the events on it that refresh the prop.
/// </summary>
internal sealed class LiveListener
{
    private readonly List<string> _events = new();

    public LiveListener(LiveChannel channel) => Channel = channel;

    public LiveChannel Channel { get; }

    public IReadOnlyList<string> Events => _events;

    public void AddEvent(string eventName)
    {
        if (!_events.Contains(eventName, StringComparer.Ordinal))
            _events.Add(eventName);
    }

    /// <summary>The protocol shape. Dictionaries, so naming policies cannot rename the protocol keys.</summary>
    public Dictionary<string, object> ToMetadata() => new(StringComparer.Ordinal)
    {
        ["channel"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = Channel.Name,
            ["type"] = Channel.TypeName
        },
        ["events"] = _events.ToList()
    };
}
