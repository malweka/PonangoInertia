namespace Ponango.Inertia;

/// <summary>
/// The live modifiers shared by every prop type that can refresh itself when a broadcast event arrives:
/// <see cref="LiveProp"/>, <see cref="OptionalProp"/>, <see cref="DeferredProp"/>, <see cref="OnceProp"/> and
/// <see cref="AlwaysProp"/>. Merge props cannot be live. Every modifier returns the prop itself.
/// </summary>
/// <typeparam name="TSelf">The concrete prop type.</typeparam>
public abstract class LiveModifiers<TSelf> : ILiveProp
    where TSelf : LiveModifiers<TSelf>
{
    private readonly LiveBehavior _live = new();

    private protected LiveModifiers()
    {
    }

    /// <summary>
    /// Refreshes the prop when <paramref name="on"/> is broadcast on <paramref name="channel"/>. The client reloads
    /// the prop, or takes its value from the event payload (<c>Inertia.BroadcastPropsAsync</c>). A string channel is
    /// public; use <see cref="LiveChannel.Private"/> or <see cref="LiveChannel.Presence"/> for the others. Calls add
    /// up; <paramref name="throttle"/> keeps the last value given.
    /// </summary>
    /// <exception cref="InvalidOperationException">The prop merges: merge props cannot be live.</exception>
    public TSelf Live(string on, LiveChannel channel, TimeSpan? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return AddLive(new[] { on }, new[] { channel }, throttle);
    }

    /// <summary>Refreshes the prop when any of <paramref name="on"/> is broadcast on <paramref name="channel"/>.</summary>
    /// <inheritdoc cref="Live(string, LiveChannel, TimeSpan?)"/>
    public TSelf Live(IEnumerable<string> on, LiveChannel channel, TimeSpan? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return AddLive(on, new[] { channel }, throttle);
    }

    /// <summary>Refreshes the prop when any of <paramref name="on"/> is broadcast on any of <paramref name="channels"/>.</summary>
    /// <inheritdoc cref="Live(string, LiveChannel, TimeSpan?)"/>
    public TSelf Live(IEnumerable<string> on, IEnumerable<LiveChannel> channels, TimeSpan? throttle = null)
        => AddLive(on, channels, throttle);

    /// <summary>
    /// Refreshes the prop when the event is broadcast, on the event's own <see cref="ILiveEvent.Channels"/>, or on
    /// <paramref name="channel"/> when given.
    /// </summary>
    /// <inheritdoc cref="Live(string, LiveChannel, TimeSpan?)"/>
    public TSelf Live(ILiveEvent on, LiveChannel? channel = null, TimeSpan? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(on);
        EnsureNotMerging();
        _live.Add(new[] { on }, channel == null ? Array.Empty<LiveChannel>() : new[] { channel }, throttle);
        return (TSelf)this;
    }

    /// <summary>Refreshes the prop when any of the events is broadcast, each on its own channels.</summary>
    /// <inheritdoc cref="Live(string, LiveChannel, TimeSpan?)"/>
    public TSelf Live(IEnumerable<ILiveEvent> on, TimeSpan? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(on);
        EnsureNotMerging();
        _live.Add(on, Array.Empty<LiveChannel>(), throttle);
        return (TSelf)this;
    }

    private protected bool IsLiveProp => _live.IsLive;

    private protected static void ThrowMergeAndLive()
        => throw new InvalidOperationException("Live updates are not supported on merge properties.");

    TSelf AddLive(IEnumerable<string> on, IEnumerable<LiveChannel> channels, TimeSpan? throttle)
    {
        ArgumentNullException.ThrowIfNull(on);
        ArgumentNullException.ThrowIfNull(channels);
        EnsureNotMerging();
        _live.Add(on, channels, throttle);
        return (TSelf)this;
    }

    void EnsureNotMerging()
    {
        if (this is IMergeableProp { ShouldMerge: true })
            ThrowMergeAndLive();
    }

    bool ILiveProp.IsLive => _live.IsLive;
    IReadOnlyList<LiveListener> ILiveProp.LiveListeners() => _live.BuildListeners();
    long? ILiveProp.LiveThrottleMilliseconds => _live.ThrottleMilliseconds;
}

/// <summary>
/// The once modifiers shared by <see cref="OptionalProp"/>, <see cref="DeferredProp"/> and <see cref="MergeProp"/>.
/// Every modifier returns the prop itself, so calls can be chained.
/// </summary>
/// <typeparam name="TSelf">The concrete prop type.</typeparam>
public abstract class OnceModifiers<TSelf> : LiveModifiers<TSelf>, IOnceableProp
    where TSelf : OnceModifiers<TSelf>
{
    private readonly OnceBehavior _once = new();

    private protected OnceModifiers()
    {
    }

    /// <summary>Remembers the value on the client once it has been loaded, so later responses skip it.</summary>
    public TSelf Once(bool once = true, string? @as = null, TimeSpan? until = null)
    {
        _once.ShouldResolveOnce = once;
        if (@as != null) _once.As(@as);
        if (until.HasValue) _once.Until(until.Value);
        return (TSelf)this;
    }

    /// <summary>Remembers the value under a custom key shared across pages. Implies <see cref="Once"/>.</summary>
    public TSelf As(string key)
    {
        _once.As(key);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    /// <summary>Sends a fresh value even if the client already remembers one.</summary>
    public TSelf Fresh(bool fresh = true)
    {
        _once.ShouldBeRefreshed = fresh;
        return (TSelf)this;
    }

    /// <summary>Expires the remembered value after <paramref name="duration"/>. Implies <see cref="Once"/>.</summary>
    public TSelf Until(TimeSpan duration)
    {
        _once.Until(duration);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    /// <summary>Expires the remembered value at <paramref name="expiresAt"/>. Implies <see cref="Once"/>.</summary>
    public TSelf Until(DateTimeOffset expiresAt)
    {
        _once.Until(expiresAt);
        _once.ShouldResolveOnce = true;
        return (TSelf)this;
    }

    bool IOnceableProp.ShouldResolveOnce => _once.ShouldResolveOnce;
    bool IOnceableProp.ShouldBeRefreshed => _once.ShouldBeRefreshed;
    string? IOnceableProp.OnceKey => _once.OnceKey;
    long? IOnceableProp.ExpiresAtMilliseconds() => _once.ExpiresAtMilliseconds();
}

/// <summary>
/// The merge modifiers shared by <see cref="DeferredProp"/> and <see cref="MergeProp"/>, on top of the once
/// modifiers.
/// </summary>
/// <typeparam name="TSelf">The concrete prop type.</typeparam>
public abstract class MergeModifiers<TSelf> : OnceModifiers<TSelf>, IMergeableProp
    where TSelf : MergeModifiers<TSelf>
{
    private protected MergeModifiers()
    {
    }

    private protected MergeBehavior MergeState { get; } = new();

    /// <summary>Deep merges the value with existing client data.</summary>
    public TSelf DeepMerge()
    {
        EnsureNotLive();
        MergeState.DeepMerge();
        return (TSelf)this;
    }

    /// <summary>Appends the value to existing client data at the root.</summary>
    public TSelf Append()
    {
        EnsureNotLive();
        MergeState.AppendAtRoot();
        return (TSelf)this;
    }

    /// <summary>
    /// Appends only the array at <paramref name="path"/> (relative to the prop) and replaces the rest of the value,
    /// optionally matching items on a field. Emits <c>mergeProps: ["prop.path"]</c>.
    /// </summary>
    public TSelf Append(string path, string? matchOn = null)
    {
        EnsureNotLive();
        MergeState.AppendAt(path, matchOn);
        return (TSelf)this;
    }

    /// <summary>Appends the arrays at each of <paramref name="paths"/>.</summary>
    public TSelf Append(IEnumerable<string> paths)
    {
        EnsureNotLive();
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) MergeState.AppendAt(path, null);
        return (TSelf)this;
    }

    /// <summary>Appends the arrays at each path, matching items on the given field (value may be null).</summary>
    public TSelf Append(IDictionary<string, string?> pathsToMatchOn)
    {
        EnsureNotLive();
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) MergeState.AppendAt(entry.Key, entry.Value);
        return (TSelf)this;
    }

    /// <summary>Prepends the value to existing client data at the root.</summary>
    public TSelf Prepend()
    {
        EnsureNotLive();
        MergeState.PrependAtRoot();
        return (TSelf)this;
    }

    /// <summary>Prepends only the array at <paramref name="path"/> (relative to the prop), optionally matching items on a field.</summary>
    public TSelf Prepend(string path, string? matchOn = null)
    {
        EnsureNotLive();
        MergeState.PrependAt(path, matchOn);
        return (TSelf)this;
    }

    /// <summary>Prepends the arrays at each of <paramref name="paths"/>.</summary>
    public TSelf Prepend(IEnumerable<string> paths)
    {
        EnsureNotLive();
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths) MergeState.PrependAt(path, null);
        return (TSelf)this;
    }

    /// <summary>Prepends the arrays at each path, matching items on the given field (value may be null).</summary>
    public TSelf Prepend(IDictionary<string, string?> pathsToMatchOn)
    {
        EnsureNotLive();
        ArgumentNullException.ThrowIfNull(pathsToMatchOn);
        foreach (var entry in pathsToMatchOn) MergeState.PrependAt(entry.Key, entry.Value);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the fields (relative to the prop, e.g. <c>id</c> or <c>messages.id</c>) used to match existing items
    /// when merging. Replaces any match fields set earlier.
    /// </summary>
    public TSelf MatchingOn(params string[] fields)
    {
        MergeState.SetMatchOn(fields);
        return (TSelf)this;
    }

    void EnsureNotLive()
    {
        if (IsLiveProp)
            ThrowMergeAndLive();
    }

    bool IMergeableProp.ShouldMerge => MergeState.ShouldMerge;
    bool IMergeableProp.ShouldDeepMerge => MergeState.ShouldDeepMerge;
    bool IMergeableProp.AppendsAtRoot => MergeState.AppendsAtRoot;
    bool IMergeableProp.PrependsAtRoot => MergeState.PrependsAtRoot;
    IReadOnlyList<string> IMergeableProp.AppendPaths => MergeState.AppendPaths;
    IReadOnlyList<string> IMergeableProp.PrependPaths => MergeState.PrependPaths;
    IReadOnlyList<string> IMergeableProp.MatchOnPaths => MergeState.MatchOnPaths;
}
