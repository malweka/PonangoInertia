using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Flash data is a top-level page object field in v3 (https://inertiajs.com/docs/v3/data-props/flash-data).
/// </summary>
public class FlashTests
{
    [Fact]
    public async Task Flash_is_included_in_initial_html_page_model()
    {
        var executor = new TestHelpers.CapturingViewResultExecutor();
        using var test = TestInfrastructure.CreateContext(
            configureServices: services => services.AddSingleton<IActionResultExecutor<ViewResult>>(executor));
        var inertia = test.GetRequiredService<InertiaContext>();

        await inertia.Render("Users/Index", new { })
            .WithFlash("message", "Welcome")
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        var page = Assert.Single(executor.Models);
        Assert.NotNull(page);
        Assert.Equal("Welcome", page!.Flash!["message"]?.ToString());
    }

    [Fact]
    public async Task Flash_is_omitted_when_nothing_was_flashed()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(
            test, test.GetRequiredService<InertiaContext>().Render("Page", new { }));

        Assert.False(document.RootElement.TryGetProperty("flash", out _));
    }

    [Fact]
    public async Task Flash_is_not_listed_in_sharedProps()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });

        using var document = await TestHelpers.ExecuteJsonAsync(
            test, inertia.Render("Page", new { }).WithFlash("message", "Saved"));

        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
        Assert.Equal("Saved", document.RootElement.GetProperty("flash").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Flash_survives_redirect_until_a_page_is_rendered()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Post;
        TestHelpers.AsInertia(httpContext);

        // POST action flashes and redirects; no page is built.
        test.GetRequiredService<InertiaContext>().Flash("message", "Created");
        await new RedirectResult("/users").ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));
        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);

        // The follow-up GET renders the page and receives the flash exactly once.
        TestHelpers.ResetResponse(httpContext);
        httpContext.Request.Method = HttpMethods.Get;
        using (var first = await TestHelpers.ExecuteJsonAsync(test, TestHelpers.FreshInertia(test).Render("Users/Index", new { })))
        {
            Assert.Equal("Created", first.RootElement.GetProperty("flash").GetProperty("message").GetString());
        }

        TestHelpers.ResetResponse(httpContext);
        using var second = await TestHelpers.ExecuteJsonAsync(test, TestHelpers.FreshInertia(test).Render("Users/Index", new { }));
        Assert.False(second.RootElement.TryGetProperty("flash", out _));
    }

    [Fact]
    public async Task WithFlash_dictionary_overload_flashes_all_values()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Page", new { })
            .WithFlash(new Dictionary<string, object?> { ["message"] = "Created", ["newUserId"] = 42 }));

        var flash = document.RootElement.GetProperty("flash");
        Assert.Equal("Created", flash.GetProperty("message").GetString());
        Assert.Equal(42, flash.GetProperty("newUserId").GetInt32());
    }

    [Fact]
    public async Task Flash_with_complex_value_round_trips()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Page", new { })
            .WithFlash("toast", new { message = "Saved", level = "success" }));

        var toast = document.RootElement.GetProperty("flash").GetProperty("toast");
        Assert.Equal("Saved", toast.GetProperty("message").GetString());
        Assert.Equal("success", toast.GetProperty("level").GetString());
    }
}
