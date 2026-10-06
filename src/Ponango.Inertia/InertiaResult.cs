using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        internal InertiaContext InertiaContext { get; set; } = null!;

        private bool? _encryptHistory;
        private bool? _clearHistory;
        private bool? _preserveFragment;
        private bool? _preserveBigIntegers;

        /// <summary>
        /// Instructs the client to encrypt this page's history entry.
        /// Overrides the global <see cref="InertiaOptions.EncryptHistory"/> default for this response.
        /// </summary>
        public InertiaResult WithEncryptHistory(bool encrypt = true)
        {
            _encryptHistory = encrypt;
            return this;
        }

        /// <summary>
        /// Instructs the client to clear all history entries when navigating to this page.
        /// Useful for login/logout transitions.
        /// </summary>
        public InertiaResult WithClearHistory(bool clear = true)
        {
            _clearHistory = clear;
            return this;
        }

        /// <summary>
        /// Instructs the client to preserve the URL fragment when navigating to this page.
        /// </summary>
        public InertiaResult WithPreserveFragment(bool preserve = true)
        {
            _preserveFragment = preserve;
            return this;
        }

        /// <summary>
        /// Sends integers outside JavaScript's safe range as <c>{"$bigint": "..."}</c> markers that the client revives as
        /// <c>BigInt</c> values. Overrides <see cref="InertiaOptions.PreserveBigIntegers"/> for this response; pass
        /// <c>false</c> to opt out when it is enabled globally.
        /// </summary>
        public InertiaResult WithPreserveBigIntegers(bool preserve = true)
        {
            _preserveBigIntegers = preserve;
            return this;
        }

        /// <summary>
        /// Adds or replaces a page prop.
        /// </summary>
        public InertiaResult With(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Props[key] = value;
            return this;
        }

        /// <summary>
        /// Flashes a one-time value. It is emitted in the page object's top-level <c>flash</c> field
        /// (not in props) and is not persisted in the client's history state.
        /// </summary>
        public InertiaResult WithFlash(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            InertiaContext.Flash(key, value);
            return this;
        }

        /// <summary>
        /// Flashes several one-time values at once. See <see cref="WithFlash(string, object)"/>.
        /// </summary>
        public InertiaResult WithFlash(IDictionary<string, object?> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            foreach (var entry in values)
                WithFlash(entry.Key, entry.Value!);

            return this;
        }

        /// <summary>
        /// Adds validation errors to the response props. When an error bag name is provided
        /// (or the X-Inertia-Error-Bag request header is present), errors are scoped under
        /// that bag name so multiple forms on the same page can have independent error sets.
        /// </summary>
        public InertiaResult WithErrors(ModelStateDictionary modelState, string? errorBag = null)
        {
            if (modelState == null || modelState.IsValid) return this;

            var errors = new Dictionary<string, string>();
            foreach (var key in modelState.Keys)
            {
                var entry = modelState[key];
                if (entry!.Errors.Count > 0)
                    errors[key] = entry.Errors[0].ErrorMessage;
            }

            if (errors.Count == 0) return this;

            // Resolve bag name: explicit arg takes priority, then request header
            var bag = errorBag ?? InertiaContext?.Headers?.ErrorBag;

            Props["errors"] = string.IsNullOrEmpty(bag)
                ? errors
                : (object)new Dictionary<string, object> { [bag] = errors };

            return this;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var httpContext = context.HttpContext;
            var request = httpContext.Request;

            if (string.IsNullOrWhiteSpace(Url))
                Url = request.Path;

            IServiceProvider serviceProvider = httpContext.RequestServices;
            var options = serviceProvider.GetService<IOptions<InertiaOptions>>()?.Value;

            if (EnsureProtocolFallback(httpContext))
                return;

            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("Ponango.Inertia")
                         ?? (ILogger)NullLogger.Instance;

            if (!InertiaContext.IsInertia)
            {
                var viewData = ViewData;
                viewData.Model = await BuildPageModelAsync(fullVisit: true, options, logger);
                var viewResult = new ViewResult
                {
                    ViewData = viewData,
                    ViewName = string.IsNullOrWhiteSpace(ViewName) ? options?.RootView : ViewName,
                };

                await viewResult.ExecuteResultAsync(context);
                return;
            }

            IJsonSerializerOptionBuilder jsonSerializerOptionBuilder = serviceProvider.GetRequiredService<IJsonSerializerOptionBuilder>();

            var pageModel = await BuildPageModelAsync(fullVisit: false, options, logger);

            var contentResult = new ContentResult
            {
                ContentType = "application/json",
                StatusCode = 200,
                Content = pageModel.ToJson(jsonSerializerOptionBuilder)
            };

            context.HttpContext.Response.Headers["X-Inertia"] = "true";

            await contentResult.ExecuteResultAsync(context);
        }

        async Task<PageModel> BuildPageModelAsync(bool fullVisit, InertiaOptions? options, ILogger logger)
        {
            var props = new List<KeyValuePair<string, object?>>(Props.Count + InertiaContext.SharedProps.Count + 1);
            var sharedPropKeys = new HashSet<string>(StringComparer.Ordinal);

            // Shared props have lower priority than page props and come first, as in the reference adapter.
            foreach (var shared in InertiaContext.SharedProps)
            {
                if (Props.ContainsKey(shared.Key))
                    continue;

                props.Add(new(shared.Key, shared.Value));
                sharedPropKeys.Add(shared.Key);
            }

            foreach (var prop in Props)
                props.Add(new(prop.Key, prop.Value));

            // The protocol requires an errors object on every page, even when there are no errors.
            if (!props.Any(prop => prop.Key == "errors"))
                props.Add(new("errors", new Dictionary<string, object>()));

            var resolver = new PropsResolver(InertiaContext.Headers, isInertia: !fullVisit, Component, logger);
            var resolvedProps = await resolver.ResolveAsync(props);

            var pageModel = new PageModel
            {
                Component = Component,
                Url = Url ?? string.Empty,
                Version = AssetsVersion,
                Props = resolvedProps
            };

            // Populate optional page object fields (only when non-empty)
            if (resolver.DeferredProps.Count > 0)
                pageModel.DeferredProps = resolver.DeferredProps;

            if (resolver.RescuedProps.Count > 0)
                pageModel.RescuedProps = resolver.RescuedProps;

            if (resolver.MergeProps.Count > 0)
                pageModel.MergeProps = resolver.MergeProps;

            if (resolver.PrependProps.Count > 0)
                pageModel.PrependProps = resolver.PrependProps;

            if (resolver.DeepMergeProps.Count > 0)
                pageModel.DeepMergeProps = resolver.DeepMergeProps;

            if (resolver.MatchPropsOn.Count > 0)
                pageModel.MatchPropsOn = resolver.MatchPropsOn;

            if (resolver.OnceProps.Count > 0)
                pageModel.OnceProps = resolver.OnceProps;

            if (resolver.ScrollProps.Count > 0)
                pageModel.ScrollProps = resolver.ScrollProps;

            // sharedProps lists only the shared keys that were actually emitted.
            sharedPropKeys.IntersectWith(resolvedProps.Keys);
            if (sharedPropKeys.Count > 0)
                pageModel.SharedProps = sharedPropKeys.ToList();

            // History encryption: per-response override takes priority, then global default
            var effectiveEncrypt = _encryptHistory ?? (options?.EncryptHistory == true ? true : (bool?)null);
            if (effectiveEncrypt == true)
                pageModel.EncryptHistory = true;

            if (_clearHistory == true)
                pageModel.ClearHistory = true;

            if (_preserveFragment == true)
                pageModel.PreserveFragment = true;

            if (_preserveBigIntegers ?? options?.PreserveBigIntegers ?? false)
                pageModel.PreserveBigIntegers = true;

            // Flash is read (and cleared) only when a page is actually built, so a version-mismatch 409
            // leaves it for the follow-up request.
            var flash = InertiaContext.PullFlash();
            if (flash.Count > 0)
                pageModel.Flash = flash;

            return pageModel;
        }

        bool EnsureProtocolFallback(HttpContext httpContext)
        {
            if (httpContext.Items.ContainsKey(InertiaMiddleware.MiddlewareEnabledItemKey))
                return false;

            InertiaMiddleware.EnsureVaryHeader(httpContext.Response.Headers);

            if (!InertiaContext.IsInertia || !HttpMethods.IsGet(httpContext.Request.Method))
                return false;

            var clientVersion = InertiaContext.Headers.Version;
            if (string.IsNullOrEmpty(clientVersion) ||
                string.Equals(clientVersion, AssetsVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            httpContext.Response.Headers["X-Inertia-Location"] = httpContext.Request.GetEncodedUrl();
            httpContext.Response.Headers["X-Inertia-Version"] = AssetsVersion;
            return true;
        }
    }
}
