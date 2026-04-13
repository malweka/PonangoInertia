using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ponango.Inertia.Tests;

public class ApiErgonomicsTests
{
    [Fact]
    public async Task Render_uses_top_level_props_and_fluent_with_helpers()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Render("Users/Index", new { users = new[] { "alice" } })
            .With("stats", new { total = 1 })
            .WithFlash("success", "created");

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var props = document.RootElement.GetProperty("props");

        Assert.True(props.TryGetProperty("users", out _));
        Assert.True(props.TryGetProperty("stats", out _));
        Assert.Equal("created", props.GetProperty("flash").GetProperty("success").GetString());
    }

    [Fact]
    public async Task Location_helper_returns_409_with_inertia_location()
    {
        using var test = TestInfrastructure.CreateContext();
        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Location("https://external.test/callback");

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.Equal(409, test.HttpContext.Response.StatusCode);
        Assert.Equal("https://external.test/callback", test.HttpContext.Response.Headers["X-Inertia-Location"].ToString());
    }

    [Fact]
    public void Static_inertia_factory_methods_create_expected_prop_types()
    {
        Assert.IsType<OptionalProp>(Inertia.Optional(() => new { }));
        Assert.IsType<AlwaysProp>(Inertia.Always(() => new { }));
        Assert.IsType<DeferredProp>(Inertia.Defer(() => new { }));
        Assert.IsType<MergeProp>(Inertia.Merge(() => new[] { 1 }));
        Assert.IsType<OnceProp>(Inertia.Once(() => new { }));
    }

    [Fact]
    public void Legacy_inertia_methods_are_marked_obsolete()
    {
        var contextMethod = typeof(InertiaExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Inertia");

        var controllerMethod = typeof(InertiaController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "Inertia");

        Assert.NotNull(contextMethod.GetCustomAttribute<ObsoleteAttribute>());
        Assert.NotNull(controllerMethod.GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void Controller_helpers_expose_request_flags_and_render()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["Purpose"] = "prefetch";
        httpContext.Request.Headers["Precognition"] = "true";

        var controller = new TestInertiaController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData()
            }
        };
        controller.ControllerContext.RouteData.Values["area"] = "Admin";
        controller.ControllerContext.RouteData.Values["controller"] = "Users";
        controller.ControllerContext.RouteData.Values["action"] = "Index";

        Assert.True(controller.CurrentIsInertia);
        Assert.True(controller.CurrentIsPrefetch);
        Assert.True(controller.CurrentIsPrecognition);

        var renderResult = controller.Render("Users/Index", new { users = new[] { "alice" } });
        Assert.True(renderResult.Props.ContainsKey("users"));

        var legacyResult = controller.Inertia("Inertia", new { }, component: null);
        Assert.Equal("Admin/Users/Index", legacyResult.Component);
    }

    private sealed class TestInertiaController : InertiaController
    {
        public bool CurrentIsInertia => IsInertia;

        public bool CurrentIsPrefetch => IsPrefetch;

        public bool CurrentIsPrecognition => IsPrecognition;
    }
}
