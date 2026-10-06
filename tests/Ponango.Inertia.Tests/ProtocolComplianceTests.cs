using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Wire-level behavior required by https://inertiajs.com/docs/v3/core-concepts/the-protocol.
/// </summary>
public class ProtocolComplianceTests
{
    [Fact]
    public async Task Once_prop_expiresAt_is_unix_milliseconds()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        var before = DateTimeOffset.UtcNow;

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Billing", new
        {
            plans = Inertia.Once(() => "pro", TimeSpan.FromHours(1))
        }));

        var expiresAt = document.RootElement.GetProperty("onceProps").GetProperty("plans").GetProperty("expiresAt").GetInt64();
        Assert.InRange(
            expiresAt,
            before.AddMinutes(59).ToUnixTimeMilliseconds(),
            before.AddHours(2).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Once_prop_without_expiry_emits_explicit_null_expiresAt()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Billing", new
        {
            plans = Inertia.Once(() => "pro")
        }));

        var entry = document.RootElement.GetProperty("onceProps").GetProperty("plans");
        Assert.Equal("plans", entry.GetProperty("prop").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Partial_data_and_except_combine_with_except_winning()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "a,b", except: "b");
        var inertia = test.GetRequiredService<InertiaContext>();

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { a = 1, b = 2, c = 3 }));

        var props = document.RootElement.GetProperty("props");
        Assert.True(props.TryGetProperty("a", out _));
        Assert.False(props.TryGetProperty("b", out _));
        Assert.False(props.TryGetProperty("c", out _));
    }

    [Fact]
    public async Task Full_visit_never_resolves_optional_or_deferred_props()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        var calls = 0;

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new
        {
            stats = Inertia.Optional(() => ++calls),
            analytics = Inertia.Defer(() => ++calls)
        }));

        var props = document.RootElement.GetProperty("props");
        Assert.False(props.TryGetProperty("stats", out _));
        Assert.False(props.TryGetProperty("analytics", out _));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Errors_prop_defaults_to_empty_object_on_full_and_partial_visits()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();

        using (var full = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { a = 1 })))
        {
            var errors = full.RootElement.GetProperty("props").GetProperty("errors");
            Assert.Equal(JsonValueKind.Object, errors.ValueKind);
            Assert.Empty(errors.EnumerateObject());
        }

        TestHelpers.ResetResponse(test.HttpContext);
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "a");

        using var partial = await TestHelpers.ExecuteJsonAsync(test, TestHelpers.FreshInertia(test).Render("Page", new { a = 1 }));
        var partialErrors = partial.RootElement.GetProperty("props").GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, partialErrors.ValueKind);
        Assert.Empty(partialErrors.EnumerateObject());
    }

    [Fact]
    public async Task Errors_prop_is_not_reported_as_shared()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { }));

        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
    }

    [Fact]
    public async Task Page_url_includes_path_base_and_query_string()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.PathBase = "/app";
        test.HttpContext.Request.Path = "/users";
        test.HttpContext.Request.QueryString = new QueryString("?page=2&search=bob");

        using var document = await TestHelpers.ExecuteJsonAsync(
            test,
            test.GetRequiredService<InertiaContext>().Render("Users/Index", new { }));

        Assert.Equal("/app/users?page=2&search=bob", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Page_url_can_still_be_set_explicitly()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Path = "/users";
        test.HttpContext.Request.QueryString = new QueryString("?page=2");

        var result = test.GetRequiredService<InertiaContext>().Render("Users/Index", new { });
        result.Url = "/people";

        using var document = await TestHelpers.ExecuteJsonAsync(test, result);

        Assert.Equal("/people", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Internal_redirect_with_fragment_returns_409_with_redirect_header()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        TestHelpers.AsInertia(httpContext);

        await RedirectThroughMiddleware(httpContext, "/article/new#section");

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("/article/new#section", httpContext.Response.Headers["X-Inertia-Redirect"].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Inertia-Location"));
        Assert.False(httpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Internal_redirect_with_fragment_after_post_returns_409_not_303()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Post;
        TestHelpers.AsInertia(httpContext);

        await RedirectThroughMiddleware(httpContext, "/article/new#section");

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("/article/new#section", httpContext.Response.Headers["X-Inertia-Redirect"].ToString());
    }

    [Fact]
    public async Task Prefetch_redirect_with_fragment_is_not_converted()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        TestHelpers.AsInertia(httpContext);
        httpContext.Request.Headers["Purpose"] = "prefetch";

        await RedirectThroughMiddleware(httpContext, "/article/new#section");

        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.Equal("/article/new#section", httpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Non_inertia_redirect_with_fragment_is_not_converted()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;

        await RedirectThroughMiddleware(httpContext, "/article/new#section");

        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Inertia-Redirect"));
    }

    [Fact]
    public async Task Controller_redirect_to_local_url_with_fragment_returns_409_with_redirect_header()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Put;
        TestHelpers.AsInertia(httpContext);

        // InertiaController.Redirect turns a local URL into an absolute same-host URL with a 303.
        var redirect = new RedirectingController().Redirect("/article/new#section");
        var middleware = new InertiaMiddleware(context =>
            redirect.ExecuteResultAsync(TestInfrastructure.CreateActionContext(context)));

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("https://app.test/article/new#section", httpContext.Response.Headers["X-Inertia-Redirect"].ToString());
    }

    [Fact]
    public async Task Protocol_fallback_version_mismatch_echoes_current_version()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/users";
        TestHelpers.AsInertia(httpContext);
        httpContext.Request.Headers["X-Inertia-Version"] = "stale-version";

        // No middleware: InertiaResult performs the version check itself.
        var inertia = test.GetRequiredService<InertiaContext>();
        await inertia.Render("Users/Index", new { }).ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("https://app.test/users", httpContext.Response.Headers["X-Inertia-Location"].ToString());
        Assert.Equal("test-version", httpContext.Response.Headers["X-Inertia-Version"].ToString());
    }

    [Fact]
    public async Task Version_mismatch_keeps_pending_flash_for_follow_up_request()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        TestHelpers.AsInertia(httpContext);
        httpContext.Request.Headers["X-Inertia-Version"] = "stale-version";
        test.GetRequiredService<InertiaContext>().Flash("message", "Saved");

        await new InertiaMiddleware(_ => Task.CompletedTask).InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        var tempData = test.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(httpContext);
        Assert.Contains("__inertia_flash_message", tempData.Keys);
    }

    [Fact]
    public void Script_payload_is_safe_with_relaxed_encoder()
    {
        var serializer = new TestHelpers.SerializerOptionsBuilder(options =>
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

        var html = TestHelpers.RenderPage(new PageModel
        {
            Component = "Page",
            Url = "/a/b",
            Version = "v",
            Props = new { value = "</SCRIPT><b>x</b><!--" }
        }, serializer);

        var body = TestHelpers.ExtractScriptBody(html);
        Assert.DoesNotContain("</", body);
        Assert.DoesNotContain("<", body);
        Assert.Contains("\\/a\\/b", body);

        using var page = JsonDocument.Parse(body);
        Assert.Equal("</SCRIPT><b>x</b><!--", page.RootElement.GetProperty("props").GetProperty("value").GetString());
        Assert.Equal("/a/b", page.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Script_payload_round_trips_with_default_encoder()
    {
        var html = TestHelpers.RenderPage(new PageModel
        {
            Component = "Page",
            Url = "/a",
            Version = "v",
            Props = new { value = "a/b<c>\"d\\e" }
        }, new TestHelpers.SerializerOptionsBuilder());

        using var page = JsonDocument.Parse(TestHelpers.ExtractScriptBody(html));
        Assert.Equal("a/b<c>\"d\\e", page.RootElement.GetProperty("props").GetProperty("value").GetString());
    }

    static Task RedirectThroughMiddleware(HttpContext httpContext, string location)
    {
        var middleware = new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = location;
            return Task.CompletedTask;
        });

        return middleware.InvokeAsync(httpContext);
    }

    private sealed class RedirectingController : InertiaController
    {
    }
}
