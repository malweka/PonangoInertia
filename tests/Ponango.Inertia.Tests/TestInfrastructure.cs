using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia.Tests;

internal static class TestInfrastructure
{
    public static TestContext CreateContext(Action<InertiaOptions>? configure = null)
    {
        var services = new ServiceCollection();
        var accessor = new HttpContextAccessor();

        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton<IAssetVersionProvider>(new TestAssetVersionProvider("test-version"));
        services.AddSingleton<ITempDataProvider, TestTempDataProvider>();
        services.AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>();
        services.AddSingleton<IUrlHelperFactory, UrlHelperFactory>();
        services.AddLogging();
        services.AddMvcCore();
        services.AddInertia(configure ?? (_ => { }));

        var root = services.BuildServiceProvider();
        var scope = root.CreateScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("app.test");
        httpContext.Request.Path = "/";
        httpContext.Response.Body = new MemoryStream();
        accessor.HttpContext = httpContext;

        return new TestContext(root, scope, accessor, httpContext);
    }

    public static ActionContext CreateActionContext(HttpContext httpContext)
        => new(httpContext, new RouteData(), new ActionDescriptor());

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponse response)
    {
        response.Body.Position = 0;
        return await JsonDocument.ParseAsync(response.Body);
    }

    public sealed class TestContext : IDisposable
    {
        private readonly ServiceProvider _root;
        private readonly IServiceScope _scope;

        public TestContext(ServiceProvider root, IServiceScope scope, IHttpContextAccessor accessor, DefaultHttpContext httpContext)
        {
            _root = root;
            _scope = scope;
            Accessor = accessor;
            HttpContext = httpContext;
        }

        public IHttpContextAccessor Accessor { get; }
        public DefaultHttpContext HttpContext { get; }
        public IServiceProvider Services => _scope.ServiceProvider;

        public T GetRequiredService<T>() where T : notnull
            => Services.GetRequiredService<T>();

        public void Dispose()
        {
            _scope.Dispose();
            _root.Dispose();
        }
    }

    private sealed class TestAssetVersionProvider : IAssetVersionProvider
    {
        private readonly string _version;

        public TestAssetVersionProvider(string version)
        {
            _version = version;
        }

        public string GetAssetVersion() => _version;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private const string StoreKey = "__temp_data_store";

        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            if (context.Items.TryGetValue(StoreKey, out var existing) &&
                existing is IDictionary<string, object> values)
            {
                return values;
            }

            var created = new Dictionary<string, object>(StringComparer.Ordinal);
            context.Items[StoreKey] = created;
            return created;
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            context.Items[StoreKey] = values;
        }
    }
}
