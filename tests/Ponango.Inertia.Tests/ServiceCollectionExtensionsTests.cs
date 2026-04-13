using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Legacy_add_inertia_overload_still_controls_serializer_options()
    {
        var services = new ServiceCollection();
        var accessor = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton<IAssetVersionProvider>(new TestAssetVersionProvider());
        services.AddSingleton<ITempDataProvider, TestTempDataProvider>();
        services.AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>();
        services.AddInertia(serializer =>
        {
            serializer.PropertyNamingPolicy = null;
        });

        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJsonSerializerOptionBuilder>();
        var json = new PageModel
        {
            Component = "Users/Index",
            Url = "/users",
            Version = "test-version",
            Props = new { Name = "Alice" }
        }.ToJson(serializer);

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("Component", out _));
        Assert.False(document.RootElement.TryGetProperty("component", out _));
    }

    private sealed class TestAssetVersionProvider : IAssetVersionProvider
    {
        public string GetAssetVersion() => "test-version";
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
