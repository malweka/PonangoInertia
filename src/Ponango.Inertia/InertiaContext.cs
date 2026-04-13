using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Ponango.Inertia;

public class InertiaContext
{
    private InertiaRequestHeaders? headers;
    private readonly HttpContext? httpContext;
    private bool? isInertia;
    private readonly InertiaFlash? flash;

    internal IAssetVersionProvider AssetVersionProvider { get; }

    public bool IsInertia => isInertia ??= IsInertiaRequest();

    public InertiaRequestHeaders Headers => headers ??= GetInertiaHeaders();

    public HttpRequest? Request => httpContext?.Request;

    /// <summary>Whether this is a prefetch request (Purpose: prefetch header).</summary>
    public bool IsPrefetch => string.Equals(Headers.Purpose, "prefetch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is a Precognition validation request.</summary>
    public bool IsPrecognition => Headers.IsPrecognition;

    public IDictionary<string, object> SharedProps { get; } = new Dictionary<string, object>();

    public InertiaContext(IHttpContextAccessor httpContextAccessor, IAssetVersionProvider assetVersionProvider, InertiaFlash? flash = null)
        : this(httpContextAccessor.HttpContext!, assetVersionProvider, flash)
    {
    }

    public InertiaContext(HttpContext httpContext, IAssetVersionProvider assetVersionProvider, InertiaFlash? flash = null)
    {
        this.httpContext = httpContext;
        AssetVersionProvider = assetVersionProvider;
        this.flash = flash;
    }

    public void Share(string key, object value)
    {
        SharedProps[key] = value;
    }

    /// <summary>
    /// Stores a one-time flash value. It will appear under the "flash" shared prop in the
    /// next Inertia response and be automatically cleared by TempData afterwards.
    /// </summary>
    public void Flash(string key, object value) => flash?.Flash(key, value);

    /// <summary>
    /// Reads pending flash values from TempData and merges them into SharedProps["flash"].
    /// Called internally by InertiaResult before building the page object.
    /// </summary>
    internal void MergeFlashIntoSharedProps()
    {
        if (flash == null) return;

        var values = flash.ReadAll();
        if (values.Count == 0) return;

        if (SharedProps.TryGetValue("flash", out var existingFlash) &&
            TryConvertToDictionary(existingFlash, out var existingValues))
        {
            foreach (var entry in values)
                existingValues[entry.Key] = entry.Value;

            SharedProps["flash"] = existingValues;
            return;
        }

        SharedProps["flash"] = new Dictionary<string, object?>(values);
    }

    static bool TryConvertToDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (value == null)
            return false;

        if (value is IDictionary<string, object> typedDictionary)
        {
            foreach (var entry in typedDictionary)
                dictionary[entry.Key] = entry.Value;

            return true;
        }

        if (value is IDictionary<string, object?> nullableDictionary)
        {
            foreach (var entry in nullableDictionary)
                dictionary[entry.Key] = entry.Value;

            return true;
        }

        try
        {
            var json = JsonSerializer.Serialize(value);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            if (parsed == null)
                return false;

            dictionary = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    bool IsInertiaRequest()
    {
        if (httpContext == null)
            return false;

        if (httpContext.Request.Headers.TryGetValue("X-Inertia", out var inertiaHeader) &&
            bool.TryParse(inertiaHeader, out var isInertiaHeader))
            return isInertiaHeader;

        return false;
    }

    InertiaRequestHeaders GetInertiaHeaders()
    {
        if (httpContext == null)
            return new InertiaRequestHeaders();

        var reqHeaders = httpContext.Request.Headers;

        reqHeaders.TryGetValue("X-Requested-With", out var requestedWith);
        reqHeaders.TryGetValue("X-Inertia-Version", out var version);
        reqHeaders.TryGetValue("X-Inertia-Partial-Data", out var partData);
        reqHeaders.TryGetValue("X-Inertia-Partial-Except", out var partExcept);
        reqHeaders.TryGetValue("X-Inertia-Partial-Component", out var partComponent);

        // v2/v3 headers
        reqHeaders.TryGetValue("X-Inertia-Reset", out var reset);
        reqHeaders.TryGetValue("X-Inertia-Error-Bag", out var errorBag);
        reqHeaders.TryGetValue("X-Inertia-Except-Once-Props", out var exceptOnceProps);
        reqHeaders.TryGetValue("Purpose", out var purpose);
        reqHeaders.TryGetValue("X-Inertia-Infinite-Scroll-Merge-Intent", out var mergeIntent);
        reqHeaders.TryGetValue("Precognition", out var precognition);
        reqHeaders.TryGetValue("Precognition-Validate-Only", out var precognitionValidateOnly);

        return new InertiaRequestHeaders
        {
            RequestedWith = requestedWith,
            Version = version,
            PartialData = partData,
            PartialExcept = partExcept,
            PartialComponent = partComponent,
            Reset = reset,
            ErrorBag = errorBag,
            ExceptOnceProps = exceptOnceProps,
            Purpose = purpose,
            MergeIntent = mergeIntent,
            IsPrecognition = string.Equals(precognition, "true", StringComparison.OrdinalIgnoreCase),
            PrecognitionValidateOnly = precognitionValidateOnly
        };
    }
}
