using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

public class InertiaMiddlewareTests
{
    [Fact]
    public async Task Shared_data_is_available_on_initial_non_inertia_requests()
    {
        using var test = TestInfrastructure.CreateContext(options =>
        {
            options.SharedData = _ => new Dictionary<string, object>
            {
                ["appName"] = "Ponango"
            };
        });

        var middleware = new InertiaMiddleware(async context =>
        {
            var inertia = context.RequestServices.GetRequiredService<InertiaContext>();
            await JsonSerializer.SerializeAsync(context.Response.Body, inertia.SharedProps);
        });

        await middleware.InvokeAsync(test.HttpContext);

        using var document = await TestInfrastructure.ReadJsonAsync(test.HttpContext.Response);
        Assert.Equal("Ponango", document.RootElement.GetProperty("appName").GetString());
    }

    [Fact]
    public async Task External_redirect_uses_location_header()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var middleware = new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "https://external.test/callback";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("https://external.test/callback", httpContext.Response.Headers["X-Inertia-Location"].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task External_redirect_with_fragment_uses_redirect_header()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var middleware = new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "https://external.test/callback#done";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("https://external.test/callback#done", httpContext.Response.Headers["X-Inertia-Redirect"].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Prefetch_requests_are_processed_normally_and_flag_is_available()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["Purpose"] = "prefetch";

        var middleware = new InertiaMiddleware(async context =>
        {
            var inertia = context.RequestServices.GetRequiredService<InertiaContext>();
            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                inertia.IsPrefetch,
                inertia.IsInertia
            });
        });

        await middleware.InvokeAsync(httpContext);

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.True(document.RootElement.GetProperty("IsPrefetch").GetBoolean());
        Assert.True(document.RootElement.GetProperty("IsInertia").GetBoolean());
    }
}
