using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// The <c>QUERY</c> HTTP method (Inertia.js client 3.10, inertia-laravel 3.6): safe and idempotent like GET, with the
/// data in the request body. For the protocol it is a non-GET method: redirects become 303 and validation errors are
/// kept across a redirect, but asset versions are only checked on GET.
/// </summary>
public class QueryMethodTests
{
    const string Query = "QUERY";

    [Fact]
    public async Task Redirect_after_query_becomes_303()
    {
        using var test = StartInertiaQuery();

        await new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/products";
            return Task.CompletedTask;
        }).InvokeAsync(test.HttpContext);

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/products", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Controller_redirect_after_query_is_303()
    {
        using var test = StartInertiaQuery();

        await new QueryController().Redirect("/products")
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("https://app.test/products", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Query_request_renders_json_page_through_the_middleware()
    {
        using var test = StartInertiaQuery();
        test.HttpContext.Request.Path = "/products";

        await new InertiaMiddleware(context =>
            context.RequestServices.GetRequiredService<InertiaContext>()
                .Render("Products/Index", new { products = new[] { "a", "b" } })
                .ExecuteResultAsync(TestInfrastructure.CreateActionContext(context)))
            .InvokeAsync(test.HttpContext);

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.Equal("true", test.HttpContext.Response.Headers["X-Inertia"].ToString());
        using var document = await TestInfrastructure.ReadJsonAsync(test.HttpContext.Response);
        Assert.Equal("Products/Index", document.RootElement.GetProperty("component").GetString());
        Assert.Equal("/products", document.RootElement.GetProperty("url").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("props").GetProperty("products").GetArrayLength());
    }

    [Fact]
    public async Task Stale_asset_version_is_not_checked_on_query_with_middleware()
    {
        using var test = StartInertiaQuery();
        test.HttpContext.Request.Headers["X-Inertia-Version"] = "stale-version";
        var reached = false;

        await new InertiaMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        }).InvokeAsync(test.HttpContext);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("X-Inertia-Location"));
    }

    [Fact]
    public async Task Stale_asset_version_is_not_checked_on_query_without_middleware()
    {
        using var test = StartInertiaQuery();
        test.HttpContext.Request.Headers["X-Inertia-Version"] = "stale-version";

        using var document = await TestHelpers.ExecuteJsonAsync(test,
            test.GetRequiredService<InertiaContext>().Render("Products/Index", new { }));

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.Equal("Products/Index", document.RootElement.GetProperty("component").GetString());
    }

    [Fact]
    public async Task Redirect_with_fragment_after_query_returns_409_with_redirect_header()
    {
        using var test = StartInertiaQuery();

        await RedirectThroughMiddleware(test.HttpContext, "/products#results");

        Assert.Equal(StatusCodes.Status409Conflict, test.HttpContext.Response.StatusCode);
        Assert.Equal("/products#results", test.HttpContext.Response.Headers["X-Inertia-Redirect"].ToString());
    }

    [Fact]
    public async Task Prefetch_query_redirect_with_fragment_is_not_converted_to_409()
    {
        using var test = StartInertiaQuery();
        test.HttpContext.Request.Headers["Purpose"] = "prefetch";

        await RedirectThroughMiddleware(test.HttpContext, "/products#results");

        // Not a fragment 409, but still a non-GET redirect, so 303.
        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/products#results", test.HttpContext.Response.Headers.Location.ToString());
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("X-Inertia-Redirect"));
    }

    [Fact]
    public async Task Partial_reload_over_query_returns_only_the_selected_props()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = Query;
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Products/Index", only: "results");

        using var document = await TestHelpers.ExecuteJsonAsync(test,
            test.GetRequiredService<InertiaContext>().Render("Products/Index", new
            {
                results = new[] { 1, 2 },
                filters = new { q = "shoes" }
            }));

        var props = document.RootElement.GetProperty("props");
        Assert.Equal(2, props.GetProperty("results").GetArrayLength());
        Assert.False(props.TryGetProperty("filters", out _));
    }

    [Fact]
    public void Valid_precognition_over_query_short_circuits_with_204()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = Query;
        test.HttpContext.Request.Headers["Precognition"] = "true";

        var executing = CreateExecutingContext(test);
        new PrecognitiveAttribute().OnActionExecuting(executing);

        var result = Assert.IsType<StatusCodeResult>(executing.Result);
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
        Assert.Equal("true", test.HttpContext.Response.Headers["Precognition-Success"].ToString());
    }

    [Fact]
    public void Invalid_precognition_over_query_returns_422_with_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = Query;
        test.HttpContext.Request.Headers["Precognition"] = "true";

        var executing = CreateExecutingContext(test);
        executing.ModelState.AddModelError("q", "The search term is required");
        new PrecognitiveAttribute().OnActionExecuting(executing);

        var result = Assert.IsType<ContentResult>(executing.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        using var errors = JsonDocument.Parse(result.Content!);
        Assert.Equal("The search term is required", errors.RootElement.GetProperty("errors").GetProperty("q").GetString());
    }

    static TestInfrastructure.TestContext StartInertiaQuery()
    {
        var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = Query;
        TestHelpers.AsInertia(test.HttpContext);
        return test;
    }

    static Task RedirectThroughMiddleware(HttpContext httpContext, string location)
        => new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = location;
            return Task.CompletedTask;
        }).InvokeAsync(httpContext);

    static ActionExecutingContext CreateExecutingContext(TestInfrastructure.TestContext test)
        => new(
            TestInfrastructure.CreateActionContext(test.HttpContext),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

    private sealed class QueryController : InertiaController
    {
    }
}
