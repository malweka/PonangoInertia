using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ponango.Inertia;

public class InertiaContext
{
    private InertiaRequestHeaders? headers;
    private readonly HttpContext? httpContext;
    private bool? isInertia;
    private readonly InertiaFlash? flash;
    private readonly InertiaValidationErrors? validationErrors;

    internal IAssetVersionProvider AssetVersionProvider { get; }

    public bool IsInertia => isInertia ??= IsInertiaRequest();

    public InertiaRequestHeaders Headers => headers ??= GetInertiaHeaders();

    public HttpRequest? Request => httpContext?.Request;

    /// <summary>Whether this is a prefetch request (Purpose: prefetch header).</summary>
    public bool IsPrefetch => string.Equals(Headers.Purpose, "prefetch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is a Precognition validation request.</summary>
    public bool IsPrecognition => Headers.IsPrecognition;

    public IDictionary<string, object> SharedProps { get; } = new Dictionary<string, object>();

    public InertiaContext(
        IHttpContextAccessor httpContextAccessor,
        IAssetVersionProvider assetVersionProvider,
        InertiaFlash? flash = null,
        InertiaValidationErrors? validationErrors = null)
        : this(httpContextAccessor.HttpContext!, assetVersionProvider, flash, validationErrors)
    {
    }

    public InertiaContext(
        HttpContext httpContext,
        IAssetVersionProvider assetVersionProvider,
        InertiaFlash? flash = null,
        InertiaValidationErrors? validationErrors = null)
    {
        this.httpContext = httpContext;
        AssetVersionProvider = assetVersionProvider;
        this.flash = flash;
        this.validationErrors = validationErrors;
    }

    public void Share(string key, object value)
    {
        SharedProps[key] = value;
    }

    /// <summary>
    /// Shares a once prop: resolved the first time a page includes it, then remembered by the client and
    /// skipped on later visits. Chain <c>As</c>, <c>Until</c> or <c>Fresh</c> on the returned prop.
    /// </summary>
    public OnceProp ShareOnce(string key, Func<object> callback)
    {
        var prop = new OnceProp(callback);
        Share(key, prop);
        return prop;
    }

    /// <inheritdoc cref="ShareOnce(string, Func{object})"/>
    public OnceProp ShareOnce(string key, Func<Task<object>> callback)
    {
        var prop = new OnceProp(callback);
        Share(key, prop);
        return prop;
    }

    /// <summary>
    /// Stores a one-time flash value. It is emitted in the top-level <c>flash</c> field of the next rendered
    /// Inertia page (read it on the client with <c>usePage().flash</c> or the <c>flash</c> event) and then cleared.
    /// It survives redirects until a page is rendered.
    /// </summary>
    public void Flash(string key, object value) => flash?.Flash(key, value);

    /// <summary>
    /// Reads and removes all pending flash values. Called internally by InertiaResult when it builds the page.
    /// </summary>
    internal IDictionary<string, object?> PullFlash()
        => flash?.ReadAll() ?? new Dictionary<string, object?>();

    /// <summary>
    /// Keeps validation errors for the next rendered Inertia page, which receives them in <c>props.errors</c>. Use it
    /// before redirecting back to a form. Each field gets its first message, or all of them when
    /// <see cref="InertiaOptions.WithAllErrors"/> is on. The errors are nested under <paramref name="errorBag"/>, else
    /// under this request's <c>X-Inertia-Error-Bag</c>, else under the next request's. Does nothing when the
    /// ModelState is valid.
    /// </summary>
    public void FlashErrors(ModelStateDictionary modelState, string? errorBag = null)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        if (modelState.IsValid) return;

        var errors = ValidationErrors.FromModelState(modelState, ValidationErrors.AllErrorsEnabled(httpContext));
        if (errors.Count > 0)
            validationErrors?.Store(errors, errorBag ?? Headers.ErrorBag);
    }

    /// <summary>
    /// Keeps validation errors (<c>field → message</c>) for the next rendered Inertia page, for validation that does
    /// not use ModelState. See <see cref="FlashErrors(ModelStateDictionary, string?)"/>.
    /// </summary>
    public void FlashErrors(IDictionary<string, string> errors, string? errorBag = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0) return;

        validationErrors?.Store(
            errors.ToDictionary(entry => entry.Key, entry => (object)entry.Value),
            errorBag ?? Headers.ErrorBag);
    }

    /// <summary>
    /// Reads and removes the stored validation errors, nested under their bag. Called internally by InertiaResult when
    /// it builds the page. Returns null when none are stored.
    /// </summary>
    internal object? PullErrors() => validationErrors?.Pull(Headers.ErrorBag);

    /// <summary>
    /// Whether <c>FlashErrors</c> stored errors during this request, so automatic capture must not replace them.
    /// </summary>
    internal bool FlashedErrorsInThisRequest => validationErrors?.StoredInThisRequest == true;

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
