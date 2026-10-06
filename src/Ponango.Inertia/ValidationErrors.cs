using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia;

/// <summary>
/// Builds the <c>props.errors</c> shape, so a re-rendered page and a page rendered after a redirect send the same JSON.
/// </summary>
internal static class ValidationErrors
{
    /// <summary>
    /// Maps each invalid field to its first message (<c>string</c>), or to all of its messages (<c>string[]</c>) when
    /// <paramref name="allErrors"/> is true.
    /// </summary>
    public static Dictionary<string, object> FromModelState(ModelStateDictionary modelState, bool allErrors)
    {
        var errors = new Dictionary<string, object>();
        foreach (var key in modelState.Keys)
        {
            var entry = modelState[key];
            if (entry!.Errors.Count > 0)
            {
                errors[key] = allErrors
                    ? entry.Errors.Select(error => error.ErrorMessage).ToArray()
                    : entry.Errors[0].ErrorMessage;
            }
        }

        return errors;
    }

    /// <summary>
    /// Nests the errors under the bag name when one is given.
    /// </summary>
    public static object Scope(Dictionary<string, object> errors, string? bag)
        => string.IsNullOrEmpty(bag)
            ? errors
            : new Dictionary<string, object> { [bag] = errors };

    /// <summary>
    /// Whether <see cref="InertiaOptions.WithAllErrors"/> is on for this request.
    /// </summary>
    public static bool AllErrorsEnabled(HttpContext? httpContext)
        => httpContext?.RequestServices?.GetService<IOptions<InertiaOptions>>()?.Value.WithAllErrors == true;
}
