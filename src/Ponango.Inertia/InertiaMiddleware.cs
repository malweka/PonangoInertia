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
                // External redirect: signal the client via 409 so it can handle navigation
                context.Response.StatusCode = 409;
                context.Response.Headers.Remove("Location");

                // URLs with a fragment use X-Inertia-Redirect; plain external use X-Inertia-Location
                var hasFragment = Uri.TryCreate(location, UriKind.Absolute, out var uri)
                    && !string.IsNullOrEmpty(uri.Fragment);

                if (hasFragment)
                    context.Response.Headers["X-Inertia-Redirect"] = location;
                else
                    context.Response.Headers["X-Inertia-Location"] = location;

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

    internal static void EnsureVaryHeader(IHeaderDictionary headers)
    {
        var vary = headers.Vary.ToString();
        if (string.IsNullOrWhiteSpace(vary))
        {
            headers.Vary = "X-Inertia";
            return;
        }

        var values = vary
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!values.Contains("X-Inertia", StringComparer.OrdinalIgnoreCase))
            headers.Vary = $"{vary}, X-Inertia";
    }

    static bool IsExternalUrl(string location, HttpRequest request)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return false; // relative URL — internal

        return !string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != (request.Host.Port ?? (request.IsHttps ? 443 : 80));
    }
}
