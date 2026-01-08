namespace Ponango.Inertia;

public record InertiaRequestHeaders
{
    public string? RequestedWith { get; init; }
    public string? Version { get; init; }
    public string? PartialData { get; init; }
    public string? PartialExcept { get; init; }
    public string? PartialComponent { get; init; }
}