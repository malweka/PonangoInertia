using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;

namespace Ponango.Inertia;

public class InertiaContext
{
    private InertiaRequestHeaders? headers;
    private HttpContext? httpContext;
    private bool? isInertia;

    public bool IsInertia => isInertia ??= IsInertiaRequest();

    public InertiaRequestHeaders Headers => headers ??= GetInertiaHeaders();
    public HttpRequest? Request => httpContext?.Request;

    public InertiaContext(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContext = httpContextAccessor.HttpContext;
    }

    public InertiaContext(HttpContext httpContext)
    {
        this.httpContext = httpContext;
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
        httpContext.Request.Headers.TryGetValue("X-Inertia-Partial-Component", out var partComponent);

        return new InertiaRequestHeaders
        {
            RequestedWith = requestedWith,
            Version = version,
            PartialData = partData,
            PartialComponent = partComponent
        };
    }
}