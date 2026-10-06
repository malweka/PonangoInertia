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
