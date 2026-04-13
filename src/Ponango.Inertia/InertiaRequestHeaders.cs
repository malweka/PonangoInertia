namespace Ponango.Inertia;

public record InertiaRequestHeaders
{
    // Original v1 headers
    public string? RequestedWith { get; init; }
    public string? Version { get; init; }
    public string? PartialData { get; init; }
    public string? PartialExcept { get; init; }
    public string? PartialComponent { get; init; }

    // v2/v3 headers
    public string? Reset { get; init; }
    public string? ErrorBag { get; init; }
    public string? ExceptOnceProps { get; init; }
    public string? Purpose { get; init; }
    public string? MergeIntent { get; init; }
    public bool IsPrecognition { get; init; }
    public string? PrecognitionValidateOnly { get; init; }
}