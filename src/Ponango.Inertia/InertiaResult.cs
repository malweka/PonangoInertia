using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        public InertiaResult WithErrors(ModelStateDictionary modelState)
        {
            if (modelState == null || modelState.IsValid) return this;

            var errors = new Dictionary<string, string>();
            foreach (var key in modelState.Keys)
            {
                var entry = modelState[key];
                if (entry.Errors.Count > 0)
                {
                    // Inertia expects a flat key-value pair for errors.
                    // We take the first error message.
                    errors[key] = entry.Errors[0].ErrorMessage;
                }
            }

            if (errors.Count > 0)
            {
                Props["errors"] = errors;
            }

            return this;
        }

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
                viewData.Model = GetPageModel(fullVisit: true);
                var viewResult = new ViewResult
                {
                    ViewData = viewData,
                    ViewName = ViewName,
                };

                await viewResult.ExecuteResultAsync(context);
                return;
            }

            IServiceProvider serviceProvider = context.HttpContext.RequestServices;
            IJsonSerializerOptionBuilder jsonSerializerOptionBuilder = serviceProvider.GetRequiredService<IJsonSerializerOptionBuilder>();

            if (!AssetsVersion.Equals(InertiaContext.Headers.Version, StringComparison.InvariantCultureIgnoreCase))
            {
                
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
                Content = GetPageModel(fullVisit: false).ToJson(jsonSerializerOptionBuilder)
            };

            context.HttpContext.Response.Headers["Vary"] = "X-Inertia";
            context.HttpContext.Response.Headers["X-Inertia"] = "true";

            await contentResult.ExecuteResultAsync(context);
        }

        PageModel GetPageModel(bool fullVisit)
        {
            var props = new Dictionary<string, object>(Props);

            // Merge Shared Props
            if (InertiaContext.SharedProps != null)
            {
                foreach (var kvp in InertiaContext.SharedProps)
                {
                    if (!props.ContainsKey(kvp.Key))
                    {
                        props[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Logic for Partial Reloads
            var partialData = InertiaContext.Headers.PartialData?.Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var partialComponent = InertiaContext.Headers.PartialComponent;

            var isPartial = !fullVisit &&
                            !string.IsNullOrEmpty(partialComponent) &&
                            partialComponent == Component &&
                            partialData != null &&
                            partialData.Any();

            if (isPartial)
            {
                // Only keep requested keys
                var keysToRemove = props.Keys.Except(partialData).ToList();
                foreach (var key in keysToRemove)
                {
                    props.Remove(key);
                }
            }
            else
            {
                // Full visit (or partial for another component): remove LazyProp unless explicitly requested (which it isn't here)
                // Actually, standard behavior:
                // Full visit: Lazy props are NOT included.
                // Partial visit (no match): Lazy props are NOT included.
                // Partial visit (match): Lazy props ARE included ONLY if requested.

                // So here, we remove all LazyProps
                var lazyKeys = props.Where(kvp => kvp.Value is LazyProp).Select(kvp => kvp.Key).ToList();
                foreach (var key in lazyKeys)
                {
                    props.Remove(key);
                }
            }

            // Evaluate remaining LazyProps
            foreach (var key in props.Keys.ToList())
            {
                if (props[key] is LazyProp lazy)
                {
                    props[key] = lazy.Invoke();
                }
            }

            return new PageModel
            {
                Component = Component,
                Url = Url,
                Version = AssetsVersion,
                Props = props
            };
        }
    }
}