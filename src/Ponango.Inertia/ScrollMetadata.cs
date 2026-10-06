namespace Ponango.Inertia;

/// <summary>
/// Pagination state for an infinite scroll prop, emitted in the page object's <c>scrollProps</c> entry.
/// Page values may be page numbers or cursor strings.
/// </summary>
public sealed class ScrollMetadata
{
    /// <summary>
    /// Creates scroll metadata. Page values should be <c>int</c>, <c>long</c>, <c>string</c> (cursor) or <c>null</c>.
    /// </summary>
    public ScrollMetadata(string pageName, object? previousPage, object? nextPage, object? currentPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);
        PageName = pageName;
        PreviousPage = previousPage;
        NextPage = nextPage;
        CurrentPage = currentPage;
    }

    /// <summary>The query string parameter the client uses for the page or cursor.</summary>
    public string PageName { get; }

    /// <summary>The previous page (or cursor), or <c>null</c> on the first page.</summary>
    public object? PreviousPage { get; }

    /// <summary>The next page (or cursor), or <c>null</c> on the last page.</summary>
    public object? NextPage { get; }

    /// <summary>The current page (or cursor).</summary>
    public object? CurrentPage { get; }

    /// <summary>Metadata for page-number pagination.</summary>
    public static ScrollMetadata ForPage(int currentPage, int? previousPage, int? nextPage, string pageName = "page")
        => new(pageName, previousPage, nextPage, currentPage);

    /// <summary>Metadata for cursor pagination.</summary>
    public static ScrollMetadata ForCursor(string? currentCursor, string? previousCursor, string? nextCursor, string cursorName = "cursor")
        => new(cursorName, previousCursor, nextCursor, currentCursor);

    // A dictionary (not an object) so null page values are still serialized, as the protocol shows them.
    internal Dictionary<string, object?> ToDictionary(bool reset) => new()
    {
        ["pageName"] = PageName,
        ["previousPage"] = PreviousPage,
        ["nextPage"] = NextPage,
        ["currentPage"] = CurrentPage,
        ["reset"] = reset
    };
}
