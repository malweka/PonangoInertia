using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Ponango.Inertia;

public class InertiaOptions
{
    /// <summary>
    /// The Razor view name used for the initial HTML response. Defaults to "Inertia".
    /// </summary>
    public string RootView { get; set; } = "Inertia";

    /// <summary>
    /// When true, instructs the client to encrypt all history entries by default.
    /// Can be overridden per-response via InertiaResult.WithEncryptHistory().
    /// </summary>
    public bool EncryptHistory { get; set; } = false;

    /// <summary>
    /// When true, integers outside JavaScript's safe range (±(2^53 − 1)) in props and flash data are sent as
    /// <c>{"$bigint": "..."}</c> markers that the client turns into native <c>BigInt</c> values, so they are not
    /// rounded. Requires Inertia client adapters 3.8.0 or later. Can be overridden per response via
    /// InertiaResult.WithPreserveBigIntegers().
    /// </summary>
    public bool PreserveBigIntegers { get; set; } = false;

    /// <summary>
    /// When true, validation errors added with InertiaResult.WithErrors() carry every message for each field
    /// (<c>string[]</c>) instead of only the first one (<c>string</c>).
    /// </summary>
    public bool WithAllErrors { get; set; } = false;

    /// <summary>
    /// When true (the default), the validation errors of an Inertia non-GET request that ends in a redirect with an
    /// invalid ModelState are kept for the next rendered page, like Laravel's redirect-back-with-errors. Set to false
    /// to keep errors only when you call InertiaContext.FlashErrors() yourself.
    /// </summary>
    public bool PersistValidationErrorsOnRedirect { get; set; } = true;

    /// <summary>
    /// When true (the default), an Inertia request whose response is an empty <c>200</c> (nothing written to the body)
    /// is redirected back to the same-host <c>Referer</c>, else to the app root, like the reference adapter's
    /// <c>onEmptyResponse</c>. Non-GET requests get a <c>303</c>. A body of any length, including <c>"0"</c>, a
    /// streamed response, and other status codes such as <c>204</c> are left alone. Requires <c>UseInertia()</c>.
    /// </summary>
    public bool RedirectEmptyResponsesBack { get; set; } = true;

    /// <summary>
    /// When true (the default), the page object lists shared prop keys in <c>sharedProps</c>, which the client uses
    /// to carry shared props over during instant visits. Set to false to omit the list; values are still sent.
    /// </summary>
    public bool ExposeSharedPropKeys { get; set; } = true;

    /// <summary>
    /// Optional delegate for injecting shared data into every Inertia response.
    /// Runs once per request, before the controller action executes.
    /// </summary>
    public Func<HttpContext, Dictionary<string, object>>? SharedData { get; set; }

    /// <summary>
    /// JSON serializer configuration. Defaults to camelCase, WhenWritingNull, IgnoreCycles.
    /// </summary>
    public Action<JsonSerializerOptions>? JsonSerializerOptions { get; set; }
}
