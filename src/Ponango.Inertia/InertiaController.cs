using System;
using Microsoft.AspNetCore.Mvc;

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

            if (string.IsNullOrWhiteSpace(assetVersion))
            {
                var version = typeof(T).Assembly.GetName().Version;
                assetVersion = InertiaExtensions.CreateMd5Hash(version != null ? version.ToString() : "1.0.0");
            }

            ViewData.Model = model;
            return new InertiaResult(component, assetVersion)
            {
                ViewData = ViewData,
                ViewName = viewName,
                Url = HttpContext.Request.Path
            };
        }
    }
}