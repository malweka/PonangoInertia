using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
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
        /// Adds validation errors to the response props. When an error bag name is provided
        /// (or the X-Inertia-Error-Bag request header is present), errors are scoped under
        /// that bag name so multiple forms on the same page can have independent error sets.
        /// </summary>
        public InertiaResult With(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Props[key] = value;
            return this;
        }

        public InertiaResult WithFlash(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            InertiaContext.Flash(key, value);
            return this;
        }

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

            // Merge any pending flash values into shared props before building the page object
            InertiaContext.MergeFlashIntoSharedProps();

            IServiceProvider serviceProvider = httpContext.RequestServices;
            var options = serviceProvider.GetService<IOptions<InertiaOptions>>()?.Value;

            if (EnsureProtocolFallback(httpContext))
                return;

            if (!InertiaContext.IsInertia)
            {
                var viewData = ViewData;
                viewData.Model = await BuildPageModelAsync(fullVisit: true, options);
                var viewResult = new ViewResult
                {
                    ViewData = viewData,
                    ViewName = string.IsNullOrWhiteSpace(ViewName) ? options?.RootView : ViewName,
                };

                await viewResult.ExecuteResultAsync(context);
                return;
            }

            IJsonSerializerOptionBuilder jsonSerializerOptionBuilder = serviceProvider.GetRequiredService<IJsonSerializerOptionBuilder>();

            var pageModel = await BuildPageModelAsync(fullVisit: false, options);

            var contentResult = new ContentResult
            {
                ContentType = "application/json",
                StatusCode = 200,
                Content = pageModel.ToJson(jsonSerializerOptionBuilder)
            };

            context.HttpContext.Response.Headers["X-Inertia"] = "true";

            await contentResult.ExecuteResultAsync(context);
        }

        async Task<PageModel> BuildPageModelAsync(bool fullVisit, InertiaOptions? options = null)
        {
            var props = new Dictionary<string, object>(Props);
            var headers = InertiaContext.Headers;
            var partialComponent = headers.PartialComponent;
            var isPartialRequest = !fullVisit &&
                                   !string.IsNullOrEmpty(partialComponent) &&
                                   partialComponent == Component;

            var partialData = isPartialRequest
                ? ParseCommaSeparated(headers.PartialData).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var partialExcept = isPartialRequest
                ? ParseCommaSeparated(headers.PartialExcept).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var exceptOnceProps = ParseCommaSeparated(headers.ExceptOnceProps).ToHashSet(StringComparer.Ordinal);
            var resetProps = ParseCommaSeparated(headers.Reset).ToHashSet(StringComparer.Ordinal);
            var mergeIntent = headers.MergeIntent;

            var sharedPropKeys = new HashSet<string>(StringComparer.Ordinal);

            // Merge shared props with lower priority than page-specific props.
            if (InertiaContext.SharedProps != null)
            {
                foreach (var kvp in InertiaContext.SharedProps)
                {
                    if (props.ContainsKey(kvp.Key))
                        continue;

                    props[kvp.Key] = kvp.Value;
                    sharedPropKeys.Add(kvp.Key);
                }
            }

            var deferredPropsMap = new Dictionary<string, List<string>>();
            var mergePropsKeys = new List<string>();
            var prependPropsKeys = new List<string>();
            var deepMergePropsKeys = new List<string>();
            var matchPropsOnKeys = new List<string>();
            var oncePropsMap = new Dictionary<string, object>();
            var scrollPropsMap = new Dictionary<string, object>();

            foreach (var key in props.Keys.ToList())
            {
                var value = props[key];
                if (!ShouldIncludeProp(
                        key,
                        value,
                        isPartialRequest,
                        partialData,
                        partialExcept,
                        exceptOnceProps,
                        deferredPropsMap,
                        oncePropsMap))
                {
                    props.Remove(key);
                    sharedPropKeys.Remove(key);
                    continue;
                }

                if (value is MergeProp mergeProp && !resetProps.Contains(key))
                {
                    var effectiveMergeMode = GetEffectiveMergeMode(mergeProp.Mode, mergeIntent);

                    switch (effectiveMergeMode)
                    {
                        case MergeMode.Append:
                            mergePropsKeys.Add(key);
                            break;
                        case MergeMode.Prepend:
                            prependPropsKeys.Add(key);
                            break;
                        case MergeMode.DeepMerge:
                            deepMergePropsKeys.Add(key);
                            break;
                    }

                    if (!string.IsNullOrEmpty(mergeProp.MatchOn))
                        matchPropsOnKeys.Add($"{key}.{mergeProp.MatchOn}");
                }

                if (value is MergeProp scrollMergeProp && scrollMergeProp.ScrollConfig != null)
                    scrollPropsMap[key] = scrollMergeProp.ScrollConfig;
            }

            // Evaluate all remaining prop wrappers to their actual values
            await EvaluatePropsAsync(props);

            // Build the page model with all metadata
            var pageModel = new PageModel
            {
                Component = Component,
                Url = Url ?? string.Empty,
                Version = AssetsVersion,
                Props = props
            };

            // Populate optional page object fields (only when non-empty)
            if (deferredPropsMap.Count > 0)
                pageModel.DeferredProps = deferredPropsMap;

            if (mergePropsKeys.Count > 0)
                pageModel.MergeProps = mergePropsKeys;

            if (prependPropsKeys.Count > 0)
                pageModel.PrependProps = prependPropsKeys;

            if (deepMergePropsKeys.Count > 0)
                pageModel.DeepMergeProps = deepMergePropsKeys;

            if (matchPropsOnKeys.Count > 0)
                pageModel.MatchPropsOn = matchPropsOnKeys;

            if (oncePropsMap.Count > 0)
                pageModel.OnceProps = oncePropsMap;

            if (scrollPropsMap.Count > 0)
                pageModel.ScrollProps = scrollPropsMap;

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
            return true;
        }

        static bool ShouldIncludeProp(
            string key,
            object value,
            bool isPartialRequest,
            HashSet<string> partialData,
            HashSet<string> partialExcept,
            HashSet<string> exceptOnceProps,
            Dictionary<string, List<string>> deferredPropsMap,
            Dictionary<string, object> oncePropsMap)
        {
            if (key == "errors" || value is AlwaysProp)
                return true;

            if (value is DeferredProp deferred)
            {
                if (!isPartialRequest)
                {
                    AddDeferredPropMetadata(deferredPropsMap, deferred.Group, key);
                    return false;
                }

                return partialData.Contains(key);
            }

            if (value is OptionalProp)
                return isPartialRequest && partialData.Contains(key);

#pragma warning disable CS0618
            if (value is LazyProp)
                return isPartialRequest && partialData.Contains(key);
#pragma warning restore CS0618

            if (value is OnceProp onceProp)
            {
                if (exceptOnceProps.Contains(key) || !PassesPartialFilter(key, value, isPartialRequest, partialData, partialExcept))
                    return false;

                AddOncePropMetadata(oncePropsMap, key, onceProp);
                return true;
            }

            return PassesPartialFilter(key, value, isPartialRequest, partialData, partialExcept);
        }

        static bool PassesPartialFilter(
            string key,
            object value,
            bool isPartialRequest,
            HashSet<string> partialData,
            HashSet<string> partialExcept)
        {
            if (!isPartialRequest)
                return true;

            if (value is AlwaysProp || key == "errors")
                return true;

            if (partialData.Count > 0)
                return partialData.Contains(key);

            return !partialExcept.Contains(key);
        }

        static void AddDeferredPropMetadata(Dictionary<string, List<string>> deferredPropsMap, string group, string key)
        {
            if (!deferredPropsMap.TryGetValue(group, out var keys))
            {
                keys = new List<string>();
                deferredPropsMap[group] = keys;
            }

            keys.Add(key);
        }

        static void AddOncePropMetadata(Dictionary<string, object> oncePropsMap, string key, OnceProp onceProp)
        {
            var entry = new Dictionary<string, object?> { ["prop"] = key };
            if (onceProp.ExpiresAfter.HasValue)
                entry["expiresAt"] = DateTimeOffset.UtcNow.Add(onceProp.ExpiresAfter.Value).ToUnixTimeSeconds();
            else
                entry["expiresAt"] = null;

            oncePropsMap[key] = entry;
        }

        static MergeMode GetEffectiveMergeMode(MergeMode configuredMode, string? mergeIntent)
        {
            if (configuredMode == MergeMode.DeepMerge)
                return configuredMode;

            return mergeIntent?.Trim().ToLowerInvariant() switch
            {
                "prepend" => MergeMode.Prepend,
                "append" => MergeMode.Append,
                _ => configuredMode
            };
        }

        /// <summary>
        /// Evaluate all prop wrapper types to their resolved values.
        /// Supports async evaluation for all prop types.
        /// </summary>
        static async Task EvaluatePropsAsync(Dictionary<string, object> props)
        {
            foreach (var key in props.Keys.ToList())
            {
                var value = props[key];
                props[key] = value switch
                {
                    AlwaysProp always => await always.InvokeAsync(),
                    OptionalProp optional => await optional.InvokeAsync(),
                    DeferredProp deferred => await deferred.InvokeAsync(),
                    MergeProp merge => await merge.InvokeAsync(),
                    OnceProp once => await once.InvokeAsync(),
#pragma warning disable CS0618
                    LazyProp lazy => lazy.Invoke(),
#pragma warning restore CS0618
                    _ => value
                };
            }
        }

        static List<string> ParseCommaSeparated(string? header)
        {
            if (string.IsNullOrEmpty(header))
                return new List<string>();

            return header
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

    }
}
