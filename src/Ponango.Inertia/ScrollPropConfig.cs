namespace Ponango.Inertia;

/// <summary>
/// Pagination metadata for Inertia infinite scroll responses.
/// Serialized into the page object's scrollProps entry for the corresponding prop key.
/// </summary>
public class ScrollPropConfig
{
    public string PageName { get; set; } = "page";

    public int? PreviousPage { get; set; }

    public int? NextPage { get; set; }

    public int CurrentPage { get; set; }
}
