using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia
{
    public abstract class InertiaController : Controller
    {
        public override RedirectResult Redirect(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentException(nameof(url));
            return new InertiaRedirectResult(url);
        }

        public InertiaResult Inertia<T>(string viewName, T model, string? component = null, string? assetVersion = null)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                var typeName = GetType().Name;
                if (typeName.EndsWith("Controller", StringComparison.InvariantCultureIgnoreCase))
                    component = typeName.Substring(0,
                        typeName.LastIndexOf("Controller", StringComparison.InvariantCultureIgnoreCase));
                else component = typeName;
            }

            var inertiaContext = HttpContext.RequestServices.GetRequiredService<InertiaContext>();

            if (string.IsNullOrWhiteSpace(assetVersion))
            {
                assetVersion = inertiaContext.AssetVersionProvider.GetAssetVersion();
            }

            ViewData.Model = model;
            return new InertiaResult(component, assetVersion)
            {
                ViewData = ViewData,
                ViewName = viewName,
                Url = HttpContext.Request.Path,
                InertiaContext = inertiaContext
            };
        }
    }
}