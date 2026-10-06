using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Shared helpers for the protocol test classes.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Marks the request as an Inertia request, optionally as a partial reload of <paramref name="partialComponent"/>.
    /// </summary>
    public static void AsInertia(
        HttpContext httpContext,
        string? partialComponent = null,
        string? only = null,
        string? except = null)
    {
        httpContext.Request.Headers["X-Inertia"] = "true";

        if (partialComponent != null)
            httpContext.Request.Headers["X-Inertia-Partial-Component"] = partialComponent;

        if (only != null)
            httpContext.Request.Headers["X-Inertia-Partial-Data"] = only;

        if (except != null)
            httpContext.Request.Headers["X-Inertia-Partial-Except"] = except;
    }

    /// <summary>
    /// Executes the result against the test context and parses the JSON page object.
    /// </summary>
    public static async Task<JsonDocument> ExecuteJsonAsync(TestInfrastructure.TestContext test, IActionResult result)
    {
        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));
        return await TestInfrastructure.ReadJsonAsync(test.HttpContext.Response);
    }

    public static void ResetResponse(HttpContext httpContext)
    {
        httpContext.Response.Body = new MemoryStream();
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.Clear();
    }

    /// <summary>
    /// A new <see cref="InertiaContext"/> over the same HTTP context and TempData store, simulating the next request.
    /// </summary>
    public static InertiaContext FreshInertia(TestInfrastructure.TestContext test)
        => new(test.HttpContext,
            test.GetRequiredService<IAssetVersionProvider>(),
            test.GetRequiredService<InertiaFlash>(),
            test.GetRequiredService<InertiaValidationErrors>());

    public static IReadOnlyList<string?> Strings(JsonElement array)
        => array.EnumerateArray().Select(x => x.GetString()).ToList();

    public static string RenderPage(PageModel page, IJsonSerializerOptionBuilder serializer, string appId = "app")
    {
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = page
        };

        var content = HtmlHelperExtensions.InertiaRender(HtmlHelperProxy.Create(viewData), serializer, appId);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    /// <summary>
    /// Returns the text between the payload script's opening tag and its closing tag.
    /// </summary>
    public static string ExtractScriptBody(string html)
    {
        var open = html.IndexOf("<script", StringComparison.Ordinal);
        var start = html.IndexOf('>', open) + 1;
        var end = html.LastIndexOf("</script>", StringComparison.Ordinal);
        return html[start..end];
    }

    internal sealed class SerializerOptionsBuilder : IJsonSerializerOptionBuilder
    {
        private readonly Action<JsonSerializerOptions>? _configure;

        public SerializerOptionsBuilder(Action<JsonSerializerOptions>? configure = null) => _configure = configure;

        public void SetSerializerOptions(JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            _configure?.Invoke(options);
        }
    }

    /// <summary>
    /// Captures the view results (and their page models) that the HTML path would render.
    /// </summary>
    internal sealed class CapturingViewResultExecutor : IActionResultExecutor<ViewResult>
    {
        public List<PageModel?> Models { get; } = new();

        public Task ExecuteAsync(ActionContext context, ViewResult result)
        {
            Models.Add(result.ViewData.Model as PageModel);
            return Task.CompletedTask;
        }
    }

    private class HtmlHelperProxy : DispatchProxy
    {
        private ViewDataDictionary ViewDataValue { get; set; } = null!;

        public static IHtmlHelper Create(ViewDataDictionary viewData)
        {
            var helper = DispatchProxy.Create<IHtmlHelper, HtmlHelperProxy>();
            ((HtmlHelperProxy)(object)helper).ViewDataValue = viewData;
            return helper;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IHtmlHelper.ViewData)}")
                return ViewDataValue;

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
