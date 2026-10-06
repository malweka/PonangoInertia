using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ponango.Inertia
{
    public static class InertiaExtensions
    {
        public static string CreateMd5Hash(string input)
        {
            // Use input string to calculate MD5 hash
            using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Convert the byte array to hexadecimal string prior to .NET 5
            var sb = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }

            return sb.ToString().ToLower();
        }

        public static InertiaResult Render<T>(
            this InertiaContext context,
            string component,
            T props,
            string? viewName = null,
            string? assetVersion = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(component);

            assetVersion ??= context.AssetVersionProvider.GetAssetVersion();

            var result = new InertiaResult(component, assetVersion)
            {
                ViewName = viewName,
                Url = context.Request is { } request ? GetPageUrl(request) : null,
                InertiaContext = context
            };

            foreach (var prop in ToPropsDictionary(props))
                result.Props[prop.Key] = prop.Value;

            return result;
        }

        public static IActionResult Location(this InertiaContext context, string url)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            return new InertiaLocationResult(context, url);
        }

        [Obsolete("Use Render() instead.")]
        public static InertiaResult Inertia<T>(this InertiaContext context, string viewName,  T model, string component, string propsName = "data")
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = typeof(T).Name;
            }

            var result = new InertiaResult(component, context.AssetVersionProvider.GetAssetVersion())
            {
                ViewName = viewName,
                Url = context.Request is { } request ? GetPageUrl(request) : null,
                InertiaContext = context
            };

            if (model != null)
            {
                result.Props.Add(propsName, model);
            }
            else
            {
                result.Props.Add(propsName, new { });
            }

            return result;
        }

        /// <summary>
        /// The page URL the client stores in its history: path base, path and query string, without scheme or host.
        /// </summary>
        internal static string GetPageUrl(HttpRequest request)
        {
            var url = $"{request.PathBase}{request.Path}{request.QueryString}";
            return url.StartsWith('/') ? url : "/" + url;
        }

        static IDictionary<string, object> ToPropsDictionary<T>(T props)
        {
            if (props == null)
                return new Dictionary<string, object>(StringComparer.Ordinal);

            if (props is IDictionary<string, object> dictionary)
                return new Dictionary<string, object>(dictionary, StringComparer.Ordinal);

            if (props is IDictionary<string, object?> nullableDictionary)
            {
                return nullableDictionary.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!,
                    StringComparer.Ordinal);
            }

            var type = props.GetType();
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                result[property.Name] = property.GetValue(props)!;
            }

            return result;
        }

        private sealed class InertiaLocationResult : IActionResult
        {
            private readonly InertiaContext _context;
            private readonly string _url;

            public InertiaLocationResult(InertiaContext context, string url)
            {
                _context = context;
                _url = url;
            }

            public Task ExecuteResultAsync(ActionContext context)
            {
                var location = ResolveLocation(context.HttpContext, _url);
                context.HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                context.HttpContext.Response.Headers["X-Inertia-Location"] = location;
                return Task.CompletedTask;
            }

            static string ResolveLocation(HttpContext httpContext, string url)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                    return absolute.ToString();

                var baseUri = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
                return new Uri(baseUri, url).ToString();
            }
        }
    }
}
