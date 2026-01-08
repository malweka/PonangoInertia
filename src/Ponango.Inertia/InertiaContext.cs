using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Ponango.Inertia;

public class InertiaContext
{
    private InertiaRequestHeaders? headers;
    private readonly HttpContext? httpContext;
    private bool? isInertia;

    internal IAssetVersionProvider AssetVersionProvider { get; }

    public bool IsInertia => isInertia ??= IsInertiaRequest();

    public InertiaRequestHeaders Headers => headers ??= GetInertiaHeaders();

    public HttpRequest? Request => httpContext?.Request;

    public IDictionary<string, object> SharedProps { get; } = new Dictionary<string, object>();

    public InertiaContext(IHttpContextAccessor httpContextAccessor, IAssetVersionProvider assetVersionProvider) : this(httpContextAccessor.HttpContext!, assetVersionProvider)
    {
    }


    public InertiaContext(HttpContext httpContext, IAssetVersionProvider assetVersionProvider)
    {
        this.httpContext = httpContext;
        AssetVersionProvider = assetVersionProvider;
    }

    public void Share(string key, object value)
    {
        SharedProps[key] = value;
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

        httpContext.Request.Headers.TryGetValue("X-Requested-With", out var requestedWith);
        httpContext.Request.Headers.TryGetValue("X-Inertia-Version", out var version);
        httpContext.Request.Headers.TryGetValue("X-Inertia-Partial-Data", out var partData);
        httpContext.Request.Headers.TryGetValue("X-Inertia-Partial-Except", out var partExcept);
        httpContext.Request.Headers.TryGetValue("X-Inertia-Partial-Component", out var partComponent);

        return new InertiaRequestHeaders
        {
            RequestedWith = requestedWith,
            Version = version,
            PartialData = partData,
            PartialExcept = partExcept,
            PartialComponent = partComponent
        };
    }
}