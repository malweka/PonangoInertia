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
    /// Optional delegate for injecting shared data into every Inertia response.
    /// Runs once per request, before the controller action executes.
    /// </summary>
    public Func<HttpContext, Dictionary<string, object>>? SharedData { get; set; }

    /// <summary>
    /// JSON serializer configuration. Defaults to camelCase, WhenWritingNull, IgnoreCycles.
    /// </summary>
    public Action<JsonSerializerOptions>? JsonSerializerOptions { get; set; }
}
