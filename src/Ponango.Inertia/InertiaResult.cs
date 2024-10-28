using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia
{
    public class InertiaResult : IActionResult
    {
        public InertiaResult(string component, string assetsVersion)
        {
            if (string.IsNullOrWhiteSpace(component))
                throw new ArgumentNullException(nameof(component));

            if (string.IsNullOrWhiteSpace(assetsVersion))
                throw new ArgumentNullException(nameof(assetsVersion));

            Component = component;
            AssetsVersion = assetsVersion;
        }

        /// <summary>
        /// Gets or sets the <see cref="ViewDataDictionary"/> for this result.
        /// </summary>
        public ViewDataDictionary ViewData { get; set; } =
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());

        public string? ViewName { get; set; }
        public string AssetsVersion { get; }
        public string Component { get; }
        public string? Url { get; set; }
        public IDictionary<string, object> Props { get; set; } = new Dictionary<string, object>();

        internal InertiaContext InertiaContext { get; set; }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var request = context.HttpContext.Request;

            if (string.IsNullOrWhiteSpace(Url))
                Url = request.Path;

            if (!InertiaContext.IsInertia)
            {
                var viewData = ViewData;
                viewData.Model = GetPageModel();
                var viewResult = new ViewResult
                {
                    ViewData = viewData,
                    ViewName = ViewName,
                };

                await viewResult.ExecuteResultAsync(context);
                return;
            }

            if (!AssetsVersion.Equals(InertiaContext.Headers.Version, StringComparison.InvariantCultureIgnoreCase))
            {
                IServiceProvider serviceProvider = context.HttpContext.RequestServices;
                IUrlHelperFactory urlHelpFactory = serviceProvider.GetRequiredService<IUrlHelperFactory>();
                IUrlHelper urlHelper = urlHelpFactory.GetUrlHelper(context);

                string redirectUri = Url;
                if (urlHelper.IsLocalUrl(redirectUri))
                {
                    redirectUri = $"{request.Scheme}://{request.Host}{urlHelper.Content(Url)}";
                }

                context.HttpContext.Response.StatusCode = 409;
                context.HttpContext.Response.Headers["X-Inertia-Location"] = redirectUri;
                return;
            }

            var contentResult = new ContentResult
            {
                ContentType = "application/json",
                StatusCode = 200,
                Content = GetPageModel().ToJson()
            };

            context.HttpContext.Response.Headers["Vary"] = "X-Inertia";
            context.HttpContext.Response.Headers["X-Inertia"] = "true";

            await contentResult.ExecuteResultAsync(context);
        }

        PageModel GetPageModel()
        {
            return new PageModel
            {
                Component = Component,
                Url = Url,
                Version = AssetsVersion,
                Props = Props
            };
        }
    }
}