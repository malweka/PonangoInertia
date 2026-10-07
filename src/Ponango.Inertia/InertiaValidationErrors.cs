using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia;

/// <summary>
/// Keeps validation errors for the next rendered Inertia page, backed by ASP.NET Core TempData, so a form action can
/// redirect back and the form page still receives <c>props.errors</c>. Registered by <c>AddInertia</c>; store errors
/// with <see cref="InertiaContext.FlashErrors(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary, string?)"/>
/// or let the built-in filter capture them when an Inertia form request redirects with an invalid ModelState.
/// </summary>
public class InertiaValidationErrors
{
    private const string TempDataKey = "__inertia_errors";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public InertiaValidationErrors(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Whether errors were stored during this request. The service is scoped, so this covers the current request
    /// only; errors still pending from an earlier request don't count.
    /// </summary>
    internal bool StoredInThisRequest { get; private set; }

    /// <summary>
    /// Stores flat errors (<c>field → string</c> or <c>field → string[]</c>) and the error bag they belong to, if
    /// any. Replaces errors stored earlier.
    /// </summary>
    internal void Store(Dictionary<string, object> errors, string? bag)
    {
        var tempData = GetTempData();
        if (tempData == null) return;

        tempData[TempDataKey] = JsonSerializer.Serialize(new
        {
            Bag = string.IsNullOrEmpty(bag) ? null : bag,
            Errors = errors
        });
        tempData.Save();
        StoredInThisRequest = true;
    }

    /// <summary>
    /// Reads and removes the stored errors. They are nested under the stored bag, else under
    /// <paramref name="requestBag"/> (the follow-up request's <c>X-Inertia-Error-Bag</c>). Returns null when none
    /// are stored.
    /// </summary>
    internal object? Pull(string? requestBag)
    {
        var tempData = GetTempData();
        if (tempData == null || !tempData.ContainsKey(TempDataKey)) return null;

        var raw = tempData[TempDataKey]?.ToString();
        tempData.Remove(TempDataKey);
        tempData.Save();

        if (string.IsNullOrEmpty(raw)) return null;

        var stored = JsonSerializer.Deserialize<StoredErrors>(raw);
        if (stored?.Errors == null) return null;

        // Rebuild plain strings and string arrays, so the errors serialize the same way WithErrors output does.
        var errors = new Dictionary<string, object>();
        foreach (var entry in stored.Errors)
        {
            errors[entry.Key] = entry.Value.ValueKind == JsonValueKind.Array
                ? entry.Value.EnumerateArray().Select(message => message.GetString() ?? string.Empty).ToArray()
                : entry.Value.GetString() ?? string.Empty;
        }

        return ValidationErrors.Scope(errors, stored.Bag ?? requestBag);
    }

    ITempDataDictionary? GetTempData()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return null;

        var factory = ctx.RequestServices.GetService<ITempDataDictionaryFactory>();
        return factory?.GetTempData(ctx);
    }

    private sealed class StoredErrors
    {
        public string? Bag { get; set; }
        public Dictionary<string, JsonElement>? Errors { get; set; }
    }
}
