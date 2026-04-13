using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia
{
    public abstract class InertiaController : Controller
    {
        protected InertiaContext InertiaContext => HttpContext.RequestServices.GetRequiredService<InertiaContext>();

        protected bool IsInertia => InertiaContext.IsInertia;

        protected bool IsPrefetch => InertiaContext.IsPrefetch;

        protected bool IsPrecognition => InertiaContext.IsPrecognition;

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

        [Obsolete("Use Render() instead.")]
        public InertiaResult Inertia<T>(string viewName, T model, string? component = null, string? assetVersion = null)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = GetDefaultComponentName();
            }

            if (string.IsNullOrWhiteSpace(assetVersion))
            {
                assetVersion = InertiaContext.AssetVersionProvider.GetAssetVersion();
            }

            ViewData.Model = model;
            return new InertiaResult(component, assetVersion)
            {
                ViewData = ViewData,
                ViewName = viewName,
                Url = HttpContext.Request.Path,
                InertiaContext = InertiaContext
            };
        }

        string GetDefaultComponentName()
        {
            var routeValues = ControllerContext.RouteData.Values;
            var area = routeValues.TryGetValue("area", out var areaValue) ? areaValue?.ToString() : null;
            var controller = routeValues.TryGetValue("controller", out var controllerValue)
                ? controllerValue?.ToString()
                : null;
            var action = routeValues.TryGetValue("action", out var actionValue)
                ? actionValue?.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(controller))
            {
                var typeName = GetType().Name;
                controller = typeName.EndsWith("Controller", StringComparison.InvariantCultureIgnoreCase)
                    ? typeName[..typeName.LastIndexOf("Controller", StringComparison.InvariantCultureIgnoreCase)]
                    : typeName;
            }

            var parts = new[] { area, controller, action }
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join("/", parts);
        }
    }
}
