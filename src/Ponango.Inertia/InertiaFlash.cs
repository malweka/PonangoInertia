using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia;

/// <summary>
/// Provides one-time flash messaging backed by ASP.NET Core TempData.
/// Flash values are emitted in the top-level <c>flash</c> field of the next rendered Inertia page
/// and are cleared once they have been read.
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
    /// Stores a flash value. It appears in the <c>flash</c> field of the next rendered page and
    /// is then cleared. The value is serialised to JSON so complex objects are supported.
    /// </summary>
    public void Flash(string key, object value)
    {
        var tempData = GetTempData();
        if (tempData == null) return;

        tempData[$"{TempDataKeyPrefix}{key}"] = JsonSerializer.Serialize(value);
        tempData.Save();
    }

    /// <summary>
    /// Reads all flash values out of TempData, removes them, and returns them as a dictionary.
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
