using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia;

public class InertiaMiddleware
{
    internal const string MiddlewareEnabledItemKey = "__Ponango.Inertia.MiddlewareEnabled";

    private readonly RequestDelegate _next;

    public InertiaMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Items[MiddlewareEnabledItemKey] = true;

        var inertiaContext = context.RequestServices.GetRequiredService<InertiaContext>();
        var options = context.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value;

        // Shared data should be available on both the initial HTML visit and subsequent Inertia requests.
        if (options?.SharedData != null)
        {
            var shared = options.SharedData(context);
            foreach (var kvp in shared)
                inertiaContext.Share(kvp.Key, kvp.Value);
        }

        // Vary: X-Inertia must be on ALL responses (HTML and JSON)
        context.Response.OnStarting(() =>
        {
            EnsureVaryHeader(context.Response.Headers);
            return Task.CompletedTask;
        });

        // Asset version check: only for Inertia GET requests
        if (inertiaContext.IsInertia && context.Request.Method == HttpMethods.Get)
        {
            var versionProvider = context.RequestServices.GetService<IAssetVersionProvider>();
            if (versionProvider != null)
            {
                var currentVersion = versionProvider.GetAssetVersion();
                var clientVersion = inertiaContext.Headers.Version;
                if (!string.IsNullOrEmpty(clientVersion) &&
                    !string.Equals(clientVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 409;
                    context.Response.Headers["X-Inertia-Location"] = context.Request.GetEncodedUrl();
                    context.Response.Headers["X-Inertia-Version"] = currentVersion;
                    return;
                }
            }
        }

        await _next(context);

        if (inertiaContext.IsInertia && !context.Response.HasStarted && IsRedirectStatus(context.Response.StatusCode))
        {
            var location = context.Response.Headers.Location.ToString();
            if (!string.IsNullOrEmpty(location) && IsExternalUrl(location, context.Request))
            {
                // External redirect: the XHR visit cannot follow it, so the client does a full
                // window.location visit instead (fragment or not).
                context.Response.StatusCode = 409;
                context.Response.Headers.Remove("Location");
                context.Response.Headers["X-Inertia-Location"] = location;
                return;
            }

            // Internal redirect whose target has a fragment: the browser would drop the fragment when
            // following the redirect, so the client makes a fresh Inertia visit to the full URL instead.
            if (!string.IsNullOrEmpty(location) && location.Contains('#') && !inertiaContext.IsPrefetch)
            {
                context.Response.StatusCode = 409;
                context.Response.Headers.Remove("Location");
                context.Response.Headers["X-Inertia-Redirect"] = location;
                return;
            }
        }

        // Convert 302 → 303 for non-GET Inertia requests (Inertia protocol requirement)
        if (inertiaContext.IsInertia
            && context.Response.StatusCode == 302
            && !HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = 303;
        }
    }

    static bool IsRedirectStatus(int statusCode)
        => statusCode is 301 or 302 or 303 or 307 or 308;

    internal static void EnsureVaryHeader(IHeaderDictionary headers) => AppendVary(headers, "X-Inertia");

    internal static void AppendVary(IHeaderDictionary headers, string value)
    {
        var vary = headers.Vary.ToString();
        if (string.IsNullOrWhiteSpace(vary))
        {
            headers.Vary = value;
            return;
        }

        var values = vary
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            headers.Vary = $"{vary}, {value}";
    }

    static bool IsExternalUrl(string location, HttpRequest request)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return false; // relative URL — internal

        if (string.IsNullOrEmpty(uri.Host))
            return false;

        return !string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != (request.Host.Port ?? (request.IsHttps ? 443 : 80));
    }
}
