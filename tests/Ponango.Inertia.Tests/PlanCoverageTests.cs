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
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

public class PlanCoverageTests
{
    [Fact]
    public void Inertia_render_emits_v3_script_payload_and_escapes_script_end_tags()
    {
        var serializer = new TestSerializerOptionsBuilder();
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = new PageModel
            {
                Component = "Users/Index",
                Url = "/users",
                Version = "test-version",
                Props = new { unsafeValue = "</script><p>bad</p>" }
            }
        };
        var helper = HtmlHelperProxy.Create(viewData);

        var html = RenderHtml(HtmlHelperExtensions.InertiaRender(helper, serializer));

        Assert.Contains("<div id=\"app\"></div>", html);
        Assert.Contains("<script type=\"application/json\"", html);
        Assert.Contains("data-page", html);
        Assert.Contains("data-inertia", html);
        Assert.DoesNotContain("</script><p>bad</p>", html);

        // Every "/" is escaped as "\/" (and "<" as <), so nothing in the payload can close the tag.
        var body = TestHelpers.ExtractScriptBody(html);
        Assert.DoesNotContain("</", body);
        Assert.Contains("\\/users", body);

        using var page = JsonDocument.Parse(body);
        Assert.Equal("/users", page.RootElement.GetProperty("url").GetString());
        Assert.Equal("</script><p>bad</p>", page.RootElement.GetProperty("props").GetProperty("unsafeValue").GetString());
    }

    [Fact]
    public void Vary_header_helper_appends_without_overwriting_existing_values()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Response.Headers.Vary = "Accept-Encoding";

        typeof(InertiaMiddleware)
            .GetMethod("EnsureVaryHeader", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { test.HttpContext.Response.Headers });

        Assert.Equal("Accept-Encoding, X-Inertia", test.HttpContext.Response.Headers.Vary.ToString());
    }

    [Fact]
    public async Task Middleware_returns_conflict_when_asset_version_mismatches()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/users";
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Version"] = "stale-version";

        var middleware = new InertiaMiddleware(_ => throw new InvalidOperationException("next should not run"));

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("https://app.test/users", httpContext.Response.Headers["X-Inertia-Location"].ToString());
        Assert.Equal("test-version", httpContext.Response.Headers["X-Inertia-Version"].ToString());
    }

    [Fact]
    public async Task Middleware_converts_non_get_inertia_302_redirects_to_303()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var middleware = new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/users";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status303SeeOther, httpContext.Response.StatusCode);
        Assert.Equal("/users", httpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Middleware_does_not_convert_internal_absolute_redirect_to_external_location()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var middleware = new InertiaMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "https://app.test/users";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.Equal("https://app.test/users", httpContext.Response.Headers.Location.ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Inertia-Location"));
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Inertia-Redirect"));
    }

    [Fact]
    public async Task Root_view_option_is_used_unless_view_name_is_explicit()
    {
        var executor = new CapturingViewResultExecutor();
        using var test = TestInfrastructure.CreateContext(
            options => options.RootView = "App",
            services => services.AddSingleton<IActionResultExecutor<ViewResult>>(executor));

        var inertia = test.GetRequiredService<InertiaContext>();
        var defaultViewResult = inertia.Render("Users/Index", new { users = Array.Empty<string>() });

        await defaultViewResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.Equal("App", executor.ViewNames.Single());

        var explicitViewResult = inertia.Render("Users/Index", new { users = Array.Empty<string>() }, viewName: "Custom");

        await explicitViewResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.Equal("Custom", executor.ViewNames.Last());
    }

    [Fact]
    public async Task Optional_and_always_props_follow_partial_reload_rules()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        var inertia = test.GetRequiredService<InertiaContext>();

        var fullResult = inertia.Render("Dashboard", new
        {
            users = new[] { "alice" },
            stats = new OptionalProp(() => new { total = 1 }),
            auth = new AlwaysProp(() => new { name = "Alice" })
        });

        await fullResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using (var full = await TestInfrastructure.ReadJsonAsync(httpContext.Response))
        {
            var props = full.RootElement.GetProperty("props");
            Assert.True(props.TryGetProperty("users", out _));
            Assert.False(props.TryGetProperty("stats", out _));
            Assert.True(props.TryGetProperty("auth", out _));
        }

        ResetResponse(httpContext);
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Dashboard";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "stats";

        inertia = CreateFreshInertia(test);
        var partialResult = inertia.Render("Dashboard", new
        {
            users = new[] { "alice" },
            stats = new OptionalProp(() => new { total = 1 }),
            auth = new AlwaysProp(() => new { name = "Alice" })
        });

        await partialResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var partial = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var partialProps = partial.RootElement.GetProperty("props");
        Assert.False(partialProps.TryGetProperty("users", out _));
        Assert.True(partialProps.TryGetProperty("stats", out _));
        Assert.True(partialProps.TryGetProperty("auth", out _));
    }

    [Fact]
    public async Task Deferred_props_emit_metadata_on_full_visit_and_resolve_when_requested()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        var inertia = test.GetRequiredService<InertiaContext>();
        var calls = 0;

        var fullResult = inertia.Render("Reports", new
        {
            analytics = new DeferredProp(() =>
            {
                calls++;
                return new { visits = 10 };
            }, group: "dashboard"),
            comments = new DeferredProp(() => new[] { "first" })
        });

        await fullResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using (var full = await TestInfrastructure.ReadJsonAsync(httpContext.Response))
        {
            Assert.False(full.RootElement.GetProperty("props").TryGetProperty("analytics", out _));
            Assert.Contains(
                full.RootElement.GetProperty("deferredProps").GetProperty("dashboard").EnumerateArray().Select(x => x.GetString()),
                value => value == "analytics");
            Assert.Contains(
                full.RootElement.GetProperty("deferredProps").GetProperty("default").EnumerateArray().Select(x => x.GetString()),
                value => value == "comments");
            Assert.Equal(0, calls);
        }

        ResetResponse(httpContext);
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Reports";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "analytics";

        inertia = CreateFreshInertia(test);
        var partialResult = inertia.Render("Reports", new
        {
            analytics = new DeferredProp(() =>
            {
                calls++;
                return new { visits = 10 };
            }, group: "dashboard")
        });

        await partialResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var partial = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.True(partial.RootElement.GetProperty("props").TryGetProperty("analytics", out _));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Once_props_emit_metadata_and_respect_except_header()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        var inertia = test.GetRequiredService<InertiaContext>();
        var calls = 0;

        var firstResult = inertia.Render("Billing", new
        {
            plans = new OnceProp(() =>
            {
                calls++;
                return new[] { "pro" };
            })
        });

        await firstResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using (var first = await TestInfrastructure.ReadJsonAsync(httpContext.Response))
        {
            Assert.True(first.RootElement.GetProperty("props").TryGetProperty("plans", out _));
            Assert.True(first.RootElement.GetProperty("onceProps").TryGetProperty("plans", out _));
            Assert.Equal(1, calls);
        }

        ResetResponse(httpContext);
        httpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";

        inertia = CreateFreshInertia(test);
        var secondResult = inertia.Render("Billing", new
        {
            plans = new OnceProp(() =>
            {
                calls++;
                return new[] { "pro" };
            })
        });

        await secondResult.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var second = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.False(second.RootElement.GetProperty("props").TryGetProperty("plans", out _));
        Assert.False(second.RootElement.TryGetProperty("onceProps", out _));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Merge_props_emit_all_metadata_shapes()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        var inertia = test.GetRequiredService<InertiaContext>();

        var result = inertia.Render("Feed", new
        {
            posts = new MergeProp(() => new[] { new { id = 1 } }, MergeMode.Append, matchOn: "id"),
            alerts = new MergeProp(() => new[] { "a" }, MergeMode.Prepend),
            settings = new MergeProp(() => new { theme = "dark" }, MergeMode.DeepMerge)
        });

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.Contains(document.RootElement.GetProperty("mergeProps").EnumerateArray().Select(x => x.GetString()), value => value == "posts");
        Assert.Contains(document.RootElement.GetProperty("prependProps").EnumerateArray().Select(x => x.GetString()), value => value == "alerts");
        Assert.Contains(document.RootElement.GetProperty("deepMergeProps").EnumerateArray().Select(x => x.GetString()), value => value == "settings");
        Assert.Contains(document.RootElement.GetProperty("matchPropsOn").EnumerateArray().Select(x => x.GetString()), value => value == "posts.id");
    }

    [Fact]
    public async Task Shared_props_metadata_reflects_final_emitted_shared_keys()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Users/Index";
        httpContext.Request.Headers["X-Inertia-Partial-Except"] = "nav";

        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });
        inertia.Share("nav", new[] { "home" });
        inertia.Share("users", new[] { "shared" });

        var result = inertia.Render("Users/Index", new { users = new[] { "page" } });

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var props = document.RootElement.GetProperty("props");
        var sharedProps = document.RootElement.GetProperty("sharedProps").EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.True(props.TryGetProperty("auth", out _));
        Assert.True(props.TryGetProperty("users", out _));
        Assert.False(props.TryGetProperty("nav", out _));
        Assert.Contains("auth", sharedProps);
        Assert.DoesNotContain("users", sharedProps);
        Assert.DoesNotContain("nav", sharedProps);
    }

    [Fact]
    public async Task With_errors_supports_flat_explicit_bag_and_header_bag_shapes()
    {
        using var flatTest = TestInfrastructure.CreateContext();
        flatTest.HttpContext.Request.Headers["X-Inertia"] = "true";
        var flatModelState = new ModelStateDictionary();
        flatModelState.AddModelError("name", "Name is required");

        await flatTest.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { })
            .WithErrors(flatModelState)
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(flatTest.HttpContext));

        using (var flat = await TestInfrastructure.ReadJsonAsync(flatTest.HttpContext.Response))
        {
            Assert.Equal("Name is required", flat.RootElement.GetProperty("props").GetProperty("errors").GetProperty("name").GetString());
        }

        using var explicitBagTest = TestInfrastructure.CreateContext();
        explicitBagTest.HttpContext.Request.Headers["X-Inertia"] = "true";
        var explicitBagModelState = new ModelStateDictionary();
        explicitBagModelState.AddModelError("email", "Email is invalid");

        await explicitBagTest.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { })
            .WithErrors(explicitBagModelState, "createUser")
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(explicitBagTest.HttpContext));

        using (var explicitBag = await TestInfrastructure.ReadJsonAsync(explicitBagTest.HttpContext.Response))
        {
            Assert.Equal("Email is invalid", explicitBag.RootElement.GetProperty("props").GetProperty("errors").GetProperty("createUser").GetProperty("email").GetString());
        }

        using var headerBagTest = TestInfrastructure.CreateContext();
        headerBagTest.HttpContext.Request.Headers["X-Inertia"] = "true";
        headerBagTest.HttpContext.Request.Headers["X-Inertia-Error-Bag"] = "updateUser";
        var headerBagModelState = new ModelStateDictionary();
        headerBagModelState.AddModelError("role", "Role is required");

        await headerBagTest.GetRequiredService<InertiaContext>()
            .Render("Users/Edit", new { })
            .WithErrors(headerBagModelState)
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(headerBagTest.HttpContext));

        using var headerBag = await TestInfrastructure.ReadJsonAsync(headerBagTest.HttpContext.Response);
        Assert.Equal("Role is required", headerBag.RootElement.GetProperty("props").GetProperty("errors").GetProperty("updateUser").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Partial_reloads_preserve_errors_even_when_not_requested()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Users/Create";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "form";

        var modelState = new ModelStateDictionary();
        modelState.AddModelError("name", "Name is required");

        await test.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { form = new { name = "" } })
            .WithErrors(modelState)
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var props = document.RootElement.GetProperty("props");
        Assert.True(props.TryGetProperty("form", out _));
        Assert.True(props.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task History_flags_respect_global_defaults_and_response_overrides()
    {
        using var globalTest = TestInfrastructure.CreateContext(options => options.EncryptHistory = true);
        globalTest.HttpContext.Request.Headers["X-Inertia"] = "true";

        await globalTest.GetRequiredService<InertiaContext>()
            .Render("Secure", new { })
            .WithClearHistory()
            .WithPreserveFragment()
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(globalTest.HttpContext));

        using (var global = await TestInfrastructure.ReadJsonAsync(globalTest.HttpContext.Response))
        {
            Assert.True(global.RootElement.GetProperty("encryptHistory").GetBoolean());
            Assert.True(global.RootElement.GetProperty("clearHistory").GetBoolean());
            Assert.True(global.RootElement.GetProperty("preserveFragment").GetBoolean());
        }

        using var overrideTest = TestInfrastructure.CreateContext(options => options.EncryptHistory = true);
        overrideTest.HttpContext.Request.Headers["X-Inertia"] = "true";

        await overrideTest.GetRequiredService<InertiaContext>()
            .Render("Public", new { })
            .WithEncryptHistory(false)
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(overrideTest.HttpContext));

        using var overriden = await TestInfrastructure.ReadJsonAsync(overrideTest.HttpContext.Response);
        Assert.False(overriden.RootElement.TryGetProperty("encryptHistory", out _));
    }

    [Fact]
    public void Precognition_validate_only_keeps_nested_keys_under_requested_prefix()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Headers["Precognition"] = "true";
        test.HttpContext.Request.Headers["Precognition-Validate-Only"] = "user";

        var filter = new PrecognitiveAttribute();
        var actionContext = TestInfrastructure.CreateActionContext(test.HttpContext);
        actionContext.ModelState.AddModelError("user.name", "Name is required");
        actionContext.ModelState.AddModelError("email", "Email is invalid");

        var executingContext = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(
            actionContext,
            new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

        filter.OnActionExecuting(executingContext);

        var result = Assert.IsType<ContentResult>(executingContext.Result);
        Assert.Equal(422, result.StatusCode);
        Assert.Contains("user.name", result.Content);
        Assert.DoesNotContain("email", result.Content);
        Assert.Equal("true", test.HttpContext.Response.Headers["Precognition"].ToString());
    }

    [Fact]
    public async Task Flash_values_are_emitted_once_and_merge_with_object_shaped_flash()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        var inertia = test.GetRequiredService<InertiaContext>();

        inertia.Share("flash", new { existing = "keep" });
        inertia.Flash("success", "created");

        await inertia.Render("Users/Index", new { })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using (var first = await TestInfrastructure.ReadJsonAsync(httpContext.Response))
        {
            var flash = first.RootElement.GetProperty("props").GetProperty("flash");
            Assert.Equal("keep", flash.GetProperty("existing").GetString());
            Assert.Equal("created", flash.GetProperty("success").GetString());
        }

        ResetResponse(httpContext);
        var secondInertia = CreateFreshInertia(test);

        await secondInertia.Render("Users/Index", new { })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var second = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.False(second.RootElement.GetProperty("props").TryGetProperty("flash", out _));
    }

    [Fact]
    public async Task Lazy_prop_and_async_wrapper_callbacks_resolve_when_requested()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Async";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "lazy,optional,always,deferred,merge,once";
        var inertia = test.GetRequiredService<InertiaContext>();

#pragma warning disable CS0618
        var result = inertia.Render("Async", new
        {
            lazy = new LazyProp(() => "legacy"),
            optional = new OptionalProp(() => Task.FromResult<object>("optional")),
            always = new AlwaysProp(() => Task.FromResult<object>("always")),
            deferred = new DeferredProp(() => Task.FromResult<object>("deferred")),
            merge = new MergeProp(() => Task.FromResult<object>(new[] { "merge" })),
            once = new OnceProp(() => Task.FromResult<object>("once"))
        });
#pragma warning restore CS0618

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var props = document.RootElement.GetProperty("props");
        Assert.Equal("legacy", props.GetProperty("lazy").GetString());
        Assert.Equal("optional", props.GetProperty("optional").GetString());
        Assert.Equal("always", props.GetProperty("always").GetString());
        Assert.Equal("deferred", props.GetProperty("deferred").GetString());
        Assert.True(props.TryGetProperty("merge", out _));
        Assert.Equal("once", props.GetProperty("once").GetString());
    }

    static string RenderHtml(IHtmlContent content)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    static void ResetResponse(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body = new MemoryStream();
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.Clear();
    }

    private sealed class TestSerializerOptionsBuilder : IJsonSerializerOptionBuilder
    {
        public void SetSerializerOptions(JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        }
    }

    private sealed class CapturingViewResultExecutor : IActionResultExecutor<ViewResult>
    {
        public List<string?> ViewNames { get; } = new();

        public Task ExecuteAsync(ActionContext context, ViewResult result)
        {
            ViewNames.Add(result.ViewName);
            return Task.CompletedTask;
        }
    }

    static InertiaContext CreateFreshInertia(TestInfrastructure.TestContext test)
    {
        return new InertiaContext(
            test.HttpContext,
            test.GetRequiredService<IAssetVersionProvider>(),
            test.GetRequiredService<InertiaFlash>());
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
