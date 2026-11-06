using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia
{
    /// <summary>
    /// A base controller with Inertia helpers. It has no member named <c>Inertia</c>, so the static
    /// <see cref="Ponango.Inertia.Inertia"/> factories (<c>Inertia.Defer(...)</c>, <c>Inertia.Live(...)</c>) work in
    /// derived controllers.
    /// </summary>
    public abstract class InertiaController : Controller
    {
        protected InertiaContext InertiaContext => HttpContext.RequestServices.GetRequiredService<InertiaContext>();

        protected bool IsInertia => InertiaContext.IsInertia;

        protected bool IsPrefetch => InertiaContext.IsPrefetch;

        protected bool IsPrecognition => InertiaContext.IsPrecognition;

        /// <summary>The client's broadcaster connection id (<c>X-Socket-Id</c>), or <c>null</c>.</summary>
        protected string? SocketId => InertiaContext.SocketId;

        public override RedirectResult Redirect(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentException(nameof(url));
            return new InertiaRedirectResult(url);
        }

        public InertiaResult Render<T>(string component, T props, string? assetVersion = null)
        {
            if (string.IsNullOrWhiteSpace(component))
                throw new ArgumentException(nameof(component));

            var result = InertiaContext.Render(component, props, assetVersion: assetVersion);
            result.ViewData = ViewData;
            return result;
        }

        public IActionResult Location(string url) => InertiaContext.Location(url);

        /// <summary>
        /// Redirects back to the page the request came from when it is on this host, else to
        /// <paramref name="fallbackUrl"/>, which must be a path on this host. Validation errors in an invalid
        /// ModelState are kept for that page.
        /// </summary>
        [NonAction]
        public RedirectResult Back(string fallbackUrl = "/") => InertiaContext.Back(fallbackUrl);
    }
}
