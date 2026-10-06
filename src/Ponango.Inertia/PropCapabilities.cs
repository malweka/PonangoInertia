namespace Ponango.Inertia;

/// <summary>
/// A prop whose value is produced lazily, only when the prop is actually included in a response.
/// </summary>
public interface IResolvableProp
{
    /// <summary>Produces the prop value.</summary>
    Task<object?> ResolveAsync();
}

/// <summary>
/// Marks a prop that is never resolved on a full (non-partial) visit.
/// Implemented by <see cref="OptionalProp"/> and <see cref="DeferredProp"/>.
/// </summary>
public interface IIgnoreFirstLoad
{
}

/// <summary>
/// A prop that is announced in the page object's <c>deferredProps</c> on a full visit and fetched by the client
/// in a follow-up partial reload.
/// </summary>
public interface IDeferrableProp
{
    /// <summary>Whether the prop is deferred.</summary>
    bool ShouldDefer { get; }

    /// <summary>The deferred group. Props in the same group are fetched in one request.</summary>
    string Group { get; }
}

/// <summary>
/// A prop whose value the client merges with existing data on partial reloads instead of replacing it.
/// </summary>
public interface IMergeableProp
{
    /// <summary>Whether merge metadata should be emitted.</summary>
    bool ShouldMerge { get; }

    /// <summary>Whether the whole value is deep merged (<c>deepMergeProps</c>).</summary>
    bool ShouldDeepMerge { get; }

    /// <summary>Whether the value is appended at the root (<c>mergeProps</c>).</summary>
    bool AppendsAtRoot { get; }

    /// <summary>Whether the value is prepended at the root (<c>prependProps</c>).</summary>
    bool PrependsAtRoot { get; }

    /// <summary>Nested paths, relative to the prop, whose arrays are appended.</summary>
    IReadOnlyList<string> AppendPaths { get; }

    /// <summary>Nested paths, relative to the prop, whose arrays are prepended.</summary>
    IReadOnlyList<string> PrependPaths { get; }

    /// <summary>
    /// Match paths relative to the prop, each ending with the key field (for example <c>data.id</c>).
    /// Named <c>MatchOnPaths</c> because <see cref="MergeProp"/> already exposes a <c>MatchOn</c> property.
    /// </summary>
    IReadOnlyList<string> MatchOnPaths { get; }
}

/// <summary>
/// A prop that the client remembers after it was resolved once, sending its key back in
/// <c>X-Inertia-Except-Once-Props</c> so the server can skip it.
/// </summary>
public interface IOnceableProp
{
    /// <summary>Whether once semantics apply.</summary>
    bool ShouldResolveOnce { get; }

    /// <summary>Whether a fresh value is sent even if the client already has one.</summary>
    bool ShouldBeRefreshed { get; }

    /// <summary>Custom key shared across pages; <c>null</c> uses the prop path.</summary>
    string? OnceKey { get; }

    /// <summary>The expiry as a Unix timestamp in milliseconds, or <c>null</c> when it never expires.</summary>
    long? ExpiresAtMilliseconds();
}

/// <summary>
/// A prop whose resolution errors can be rescued: the prop is omitted, the exception is logged, and the key is
/// listed in the page object's <c>rescuedProps</c>.
/// </summary>
public interface IRescuableProp
{
    /// <summary>Whether resolution errors are rescued.</summary>
    bool ShouldRescue { get; }
}

/// <summary>
/// A mergeable prop whose merge shape depends on the <c>X-Inertia-Infinite-Scroll-Merge-Intent</c> header.
/// </summary>
internal interface IMergeIntentAware
{
    IMergeableProp ForMergeIntent(string? mergeIntent);
}

/// <summary>
/// A prop that contributes an entry to the page object's <c>scrollProps</c>.
/// </summary>
internal interface IScrollMetadataProvider
{
    /// <summary>Returns the scroll metadata, or <c>null</c> when the prop has none.</summary>
    object? GetScrollMetadata(object? resolvedValue, bool reset);
}
