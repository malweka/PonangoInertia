namespace Ponango.Inertia;

/// <summary>
/// Static factory methods for Inertia prop wrappers.
/// </summary>
public static class Inertia
{
    public static OptionalProp Optional(Func<object> callback) => new(callback);

    public static OptionalProp Optional(Func<Task<object>> callback) => new(callback);

    public static AlwaysProp Always(Func<object> callback) => new(callback);

    public static AlwaysProp Always(Func<Task<object>> callback) => new(callback);

    /// <summary>
    /// Defers the prop to a follow-up request. With <paramref name="rescue"/>, a failing callback omits the prop
    /// and lists it in <c>rescuedProps</c> instead of failing the response.
    /// </summary>
    public static DeferredProp Defer(Func<object> callback, string group = "default", bool rescue = false)
        => new(callback, group, rescue);

    /// <inheritdoc cref="Defer(Func{object}, string, bool)"/>
    public static DeferredProp Defer(Func<Task<object>> callback, string group = "default", bool rescue = false)
        => new(callback, group, rescue);

    public static MergeProp Merge(Func<object> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    public static MergeProp Merge(Func<Task<object>> callback, MergeMode mode = MergeMode.Append, string? matchOn = null)
        => new(callback, mode, matchOn);

    /// <summary>Deep merges the whole value with existing client data.</summary>
    public static MergeProp DeepMerge(Func<object> callback) => new(callback, MergeMode.DeepMerge);

    /// <inheritdoc cref="DeepMerge(Func{object})"/>
    public static MergeProp DeepMerge(Func<Task<object>> callback) => new(callback, MergeMode.DeepMerge);

    /// <summary>
    /// An infinite scroll prop. <paramref name="metadata"/> receives the resolved value and returns its pagination
    /// state; the array under <paramref name="wrapper"/> is merged (<c>mergeProps: ["prop.data"]</c>).
    /// </summary>
    public static ScrollProp Scroll(Func<object> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")
        => new(value, metadata, wrapper);

    /// <inheritdoc cref="Scroll(Func{object}, Func{object, ScrollMetadata}, string)"/>
    public static ScrollProp Scroll(Func<Task<object>> value, Func<object, ScrollMetadata> metadata, string wrapper = "data")
        => new(value, metadata, wrapper);

    /// <summary>
    /// An infinite scroll prop with known pagination state; the array under <paramref name="wrapper"/> is merged.
    /// </summary>
    public static ScrollProp Scroll(Func<object> value, ScrollMetadata metadata, string wrapper = "data")
        => new(value, metadata, wrapper);

    /// <inheritdoc cref="Scroll(Func{object}, ScrollMetadata, string)"/>
    public static ScrollProp Scroll(Func<Task<object>> value, ScrollMetadata metadata, string wrapper = "data")
        => new(value, metadata, wrapper);

    public static OnceProp Once(Func<object> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);

    /// <summary>
    /// A prop the client refreshes when <paramref name="on"/> is broadcast on <paramref name="channel"/>. A string
    /// channel is public; use <see cref="LiveChannel.Private"/> for a private one. <paramref name="value"/> may be a
    /// delegate without parameters (<c>() => order</c>), evaluated only when the prop is included.
    /// </summary>
    public static LiveProp Live(object? value, string on, LiveChannel channel, TimeSpan? throttle = null)
        => new LiveProp(value).Live(on, channel, throttle);

    /// <inheritdoc cref="Live(object?, string, LiveChannel, TimeSpan?)"/>
    public static LiveProp Live(object? value, IEnumerable<string> on, LiveChannel channel, TimeSpan? throttle = null)
        => new LiveProp(value).Live(on, channel, throttle);

    /// <inheritdoc cref="Live(object?, string, LiveChannel, TimeSpan?)"/>
    public static LiveProp Live(object? value, IEnumerable<string> on, IEnumerable<LiveChannel> channels, TimeSpan? throttle = null)
        => new LiveProp(value).Live(on, channels, throttle);

    /// <summary>
    /// A prop the client refreshes when <paramref name="on"/> is broadcast, on the event's own channels or on
    /// <paramref name="channel"/> when given.
    /// </summary>
    public static LiveProp Live(object? value, ILiveEvent on, LiveChannel? channel = null, TimeSpan? throttle = null)
        => new LiveProp(value).Live(on, channel, throttle);

    /// <summary>A prop the client refreshes when any of the events is broadcast, each on its own channels.</summary>
    public static LiveProp Live(object? value, IEnumerable<ILiveEvent> on, TimeSpan? throttle = null)
        => new LiveProp(value).Live(on, throttle);

    /// <summary>
    /// Builds the payload of a broadcast event that carries new prop values, so clients take them from the event
    /// instead of reloading: <c>{ "__inertia": { "props": { ... }, "event": ..., "socketId": ... } }</c>.
    /// <para>
    /// Values are resolved like page props: wrappers and lazy delegates are evaluated, nested anonymous objects and
    /// string-keyed dictionaries are walked, and rescued deferred props are left out (not sent as null). Keys are
    /// prop paths and stay literal, so a nested live prop is sent as <c>["auth.order"] = ...</c>. Every subscriber
    /// receives the same payload: only send values that are the same for everyone, and let user-specific props
    /// reload instead.
    /// </para>
    /// <para>
    /// <paramref name="event"/> fills <c>__inertia.event</c>, which channel transports (such as SignalR) read to
    /// route the event. <paramref name="socketId"/> fills <c>__inertia.socketId</c>, so the client that caused the
    /// change drops the event when the broadcaster cannot exclude it. Both are left out when null. Serialize the
    /// result with the broadcaster's own JSON settings.
    /// </para>
    /// </summary>
    public static Task<IDictionary<string, object?>> BroadcastPropsAsync(object props, string? @event = null, string? socketId = null)
        => BroadcastPropsAsync(props, @event, socketId, logger: null);

    /// <inheritdoc cref="BroadcastPropsAsync(object, string?, string?)"/>
    /// <param name="props">The props to send, keyed by prop path.</param>
    /// <param name="event">The event name for <c>__inertia.event</c>, or null.</param>
    /// <param name="socketId">The connection that caused the change, for <c>__inertia.socketId</c>, or null.</param>
    /// <param name="logger">Logs the exceptions of rescued props.</param>
    public static async Task<IDictionary<string, object?>> BroadcastPropsAsync(
        object props,
        string? @event,
        string? socketId,
        Microsoft.Extensions.Logging.ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(props);

        var resolver = new PropsResolver(
            new InertiaRequestHeaders(),
            isInertia: false,
            component: string.Empty,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["props"] = await resolver.ResolveBroadcastPropsAsync(props)
        };

        if (@event != null)
            envelope["event"] = @event;

        if (socketId != null)
            envelope["socketId"] = socketId;

        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["__inertia"] = envelope };
    }

    public static OnceProp Once(Func<Task<object>> callback, TimeSpan? expiresAfter = null) => new(callback, expiresAfter);
}
