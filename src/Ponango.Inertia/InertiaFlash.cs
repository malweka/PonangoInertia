using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia;

/// <summary>
/// Provides one-time flash messaging backed by ASP.NET Core TempData.
/// Flash values are written into shared props for the next Inertia response
/// and are automatically cleared afterwards by TempData semantics.
/// </summary>
public class InertiaFlash
{
    private const string TempDataKeyPrefix = "__inertia_flash_";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public InertiaFlash(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Stores a flash value. It will appear in shared props on the very next response and
    /// then be cleared. The value is serialised to JSON so complex objects are supported.
    /// </summary>
    public void Flash(string key, object value)
    {
        var tempData = GetTempData();
        if (tempData == null) return;

        tempData[$"{TempDataKeyPrefix}{key}"] = JsonSerializer.Serialize(value);
        tempData.Save();
    }

    /// <summary>
    /// Reads all flash values out of TempData and returns them as a dictionary.
    /// Calling Keep() is NOT called here — TempData will remove them after the response.
    /// </summary>
    internal Dictionary<string, object?> ReadAll()
    {
        var result = new Dictionary<string, object?>();
        var tempData = GetTempData();
        if (tempData == null) return result;

        foreach (var key in tempData.Keys.Where(k => k.StartsWith(TempDataKeyPrefix)).ToList())
        {
            var flashKey = key[TempDataKeyPrefix.Length..];
            var raw = tempData[key]?.ToString();
            result[flashKey] = raw != null
                ? JsonSerializer.Deserialize<object>(raw)
                : null;

            tempData.Remove(key);
        }

        tempData.Save();
        return result;
    }

    ITempDataDictionary? GetTempData()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return null;

        var factory = ctx.RequestServices.GetService<ITempDataDictionaryFactory>();
        return factory?.GetTempData(ctx);
    }
}
