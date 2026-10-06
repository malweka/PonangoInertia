using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Validation errors carried across a redirect to the next rendered page, like Laravel's redirect-back-with-errors
/// (https://inertiajs.com/docs/v3/the-basics/validation).
/// </summary>
public class ValidationErrorRedirectTests
{
    [Fact]
    public async Task Invalid_model_state_on_inertia_post_redirect_is_shown_on_next_page()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        var redirect = new RedirectResult("/users/create");
        RunFilter(test, Invalid("email", "Email is required"), redirect);
        await redirect.ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Redirect_to_action_result_stores_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        RunFilter(test, Invalid("email", "Email is required"), new RedirectToActionResult("Create", "Users", null));

        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Errors_are_consumed_once()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        using (var first = await RenderNextPageAsync(test))
        {
            Assert.True(Errors(first).TryGetProperty("email", out _));
        }

        using var second = await RenderNextPageAsync(test);
        AssertNoErrors(second);
    }

    [Fact]
    public async Task Non_inertia_post_redirect_does_not_store_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = HttpMethods.Post;

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        TestHelpers.AsInertia(test.HttpContext);
        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Get_redirect_does_not_store_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Method = HttpMethods.Get;

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Valid_model_state_does_not_store_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        RunFilter(test, new ModelStateDictionary(), new RedirectResult("/users"));

        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Non_redirect_result_does_not_store_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        RunFilter(test, Invalid("email", "Email is required"), new ContentResult { Content = "nope" });
        RunFilter(test, Invalid("email", "Email is required"),
            test.GetRequiredService<InertiaContext>().Render("Users/Create", new { }));

        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Precognition_request_does_not_store_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        test.HttpContext.Request.Headers["Precognition"] = "true";

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        test.HttpContext.Request.Headers.Remove("Precognition");
        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Error_bag_header_on_post_scopes_stored_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        test.HttpContext.Request.Headers["X-Inertia-Error-Bag"] = "createUser";

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        // The bag stored with the errors wins over a different bag on the follow-up request.
        test.HttpContext.Request.Headers["X-Inertia-Error-Bag"] = "otherForm";
        using var document = await RenderNextPageAsync(test);
        var errors = Errors(document);
        Assert.Equal("Email is required", errors.GetProperty("createUser").GetProperty("email").GetString());
        Assert.False(errors.TryGetProperty("otherForm", out _));
    }

    [Fact]
    public async Task Error_bag_header_on_follow_up_get_scopes_flat_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        test.HttpContext.Request.Headers["X-Inertia-Error-Bag"] = "createUser";
        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required",
            Errors(document).GetProperty("createUser").GetProperty("email").GetString());
    }

    [Fact]
    public async Task WithAllErrors_option_stores_arrays()
    {
        using var test = TestInfrastructure.CreateContext(options => options.WithAllErrors = true);
        StartInertiaPost(test);

        RunFilter(test, Invalid("email", "Email is required", "Email is invalid"), new RedirectResult("/users/create"));

        using var document = await RenderNextPageAsync(test);
        Assert.Equal(new[] { "Email is required", "Email is invalid" },
            TestHelpers.Strings(Errors(document).GetProperty("email")));
    }

    [Fact]
    public async Task Explicit_WithErrors_on_the_rendered_page_wins_and_stored_errors_are_cleared()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        PrepareNextRequest(test);
        using (var first = await TestHelpers.ExecuteJsonAsync(test, TestHelpers.FreshInertia(test)
                   .Render("Users/Create", new { })
                   .WithErrors(Invalid("name", "Name is required"))))
        {
            var errors = Errors(first);
            Assert.Equal("Name is required", errors.GetProperty("name").GetString());
            Assert.False(errors.TryGetProperty("email", out _));
        }

        using var second = await RenderNextPageAsync(test);
        AssertNoErrors(second);
    }

    [Fact]
    public async Task Shared_errors_prop_wins_over_stored_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        PrepareNextRequest(test);
        var inertia = TestHelpers.FreshInertia(test);
        inertia.Share("errors", new Dictionary<string, object> { ["custom"] = "From the app" });
        using (var first = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Users/Create", new { })))
        {
            var errors = Errors(first);
            Assert.Equal("From the app", errors.GetProperty("custom").GetString());
            Assert.False(errors.TryGetProperty("email", out _));
        }

        using var second = await RenderNextPageAsync(test);
        AssertNoErrors(second);
    }

    [Fact]
    public async Task FlashErrors_with_model_state_stores_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        test.GetRequiredService<InertiaContext>().FlashErrors(Invalid("email", "Email is required"), "createUser");

        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required",
            Errors(document).GetProperty("createUser").GetProperty("email").GetString());
    }

    [Fact]
    public async Task FlashErrors_with_dictionary_stores_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);

        test.GetRequiredService<InertiaContext>().FlashErrors(new Dictionary<string, string>
        {
            ["email"] = "Email is taken",
            ["name"] = "Name is required"
        });

        using var document = await RenderNextPageAsync(test);
        var errors = Errors(document);
        Assert.Equal("Email is taken", errors.GetProperty("email").GetString());
        Assert.Equal("Name is required", errors.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Option_off_disables_automatic_capture()
    {
        using var test = TestInfrastructure.CreateContext(options => options.PersistValidationErrorsOnRedirect = false);
        StartInertiaPost(test);

        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        using var document = await RenderNextPageAsync(test);
        AssertNoErrors(document);
    }

    [Fact]
    public async Task Version_mismatch_409_through_middleware_keeps_stored_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        PrepareNextRequest(test);
        test.HttpContext.Request.Headers["X-Inertia-Version"] = "old-version";

        // The middleware resolves InertiaContext from the request scope, so the next request needs its own scope.
        using var nextRequestScope = test.GetRequiredService<IServiceScopeFactory>().CreateScope();
        test.HttpContext.RequestServices = nextRequestScope.ServiceProvider;
        var middleware = new InertiaMiddleware(context =>
            TestHelpers.FreshInertia(test).Render("Users/Create", new { }).ExecuteResultAsync(
                TestInfrastructure.CreateActionContext(context)));
        await middleware.InvokeAsync(test.HttpContext);
        Assert.Equal(StatusCodes.Status409Conflict, test.HttpContext.Response.StatusCode);

        test.HttpContext.Request.Headers.Remove("X-Inertia-Version");
        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Version_mismatch_409_without_middleware_keeps_stored_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        PrepareNextRequest(test);
        test.HttpContext.Request.Headers["X-Inertia-Version"] = "old-version";
        await TestHelpers.FreshInertia(test).Render("Users/Create", new { })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));
        Assert.Equal(StatusCodes.Status409Conflict, test.HttpContext.Response.StatusCode);

        test.HttpContext.Request.Headers.Remove("X-Inertia-Version");
        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Stored_errors_survive_partial_reload_filtering()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        RunFilter(test, Invalid("email", "Email is required"), new RedirectResult("/users/create"));

        PrepareNextRequest(test);
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Users/Create", only: "form");
        using var document = await TestHelpers.ExecuteJsonAsync(test,
            TestHelpers.FreshInertia(test).Render("Users/Create", new { form = new { email = "" }, other = 1 }));

        var props = document.RootElement.GetProperty("props");
        Assert.Equal("Email is required", props.GetProperty("errors").GetProperty("email").GetString());
        Assert.False(props.TryGetProperty("other", out _));
    }

    [Fact]
    public async Task Back_result_is_captured_by_the_filter()
    {
        using var test = TestInfrastructure.CreateContext();
        StartInertiaPost(test);
        test.HttpContext.Request.Headers.Referer = "https://app.test/users/create";

        RunFilter(test, Invalid("email", "Email is required"), test.GetRequiredService<InertiaContext>().Back());

        using var document = await RenderNextPageAsync(test);
        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("https://app.test/users/create?step=2", "/users/create?step=2")]
    [InlineData("/users/create", "/users/create")]
    [InlineData("https://evil.test/users/create", "/fallback")]
    [InlineData("https://app.test:8443/users/create", "/fallback")]
    [InlineData("//evil.test/users/create", "/fallback")]
    [InlineData("/\\evil.test/users/create", "/fallback")]
    [InlineData("javascript:alert(1)", "/fallback")]
    [InlineData("", "/fallback")]
    public void Back_redirects_to_same_host_referer_else_fallback(string referer, string expected)
    {
        using var test = TestInfrastructure.CreateContext();
        if (referer.Length > 0)
            test.HttpContext.Request.Headers.Referer = referer;

        var result = test.GetRequiredService<InertiaContext>().Back("/fallback");

        Assert.Equal(expected, result.Url);
    }

    [Fact]
    public void Back_defaults_to_the_root_fallback()
    {
        using var test = TestInfrastructure.CreateContext();

        Assert.Equal("/", test.GetRequiredService<InertiaContext>().Back().Url);
    }

    [Fact]
    public async Task Back_from_a_put_returns_303()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Method = HttpMethods.Put;
        test.HttpContext.Request.Headers.Referer = "https://app.test/users/1/edit";

        await test.GetRequiredService<InertiaContext>().Back()
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("https://app.test/users/1/edit", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public void Controller_Back_uses_the_referer()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Headers.Referer = "https://app.test/users/create";
        var controller = new TestInertiaController
        {
            ControllerContext = new ControllerContext { HttpContext = test.HttpContext, RouteData = new RouteData() }
        };

        Assert.Equal("/users/create", controller.Back("/fallback").Url);
    }

    [Fact]
    public void Filter_is_registered_by_AddInertia()
    {
        using var test = TestInfrastructure.CreateContext();

        Assert.Single(FilterFactories(test));
    }

    static void StartInertiaPost(TestInfrastructure.TestContext test)
    {
        test.HttpContext.Request.Method = HttpMethods.Post;
        TestHelpers.AsInertia(test.HttpContext);
    }

    /// <summary>
    /// Simulates the next request: a GET Inertia visit on the same HTTP context and TempData store.
    /// </summary>
    static void PrepareNextRequest(TestInfrastructure.TestContext test)
    {
        TestHelpers.ResetResponse(test.HttpContext);
        test.HttpContext.Request.Method = HttpMethods.Get;
        TestHelpers.AsInertia(test.HttpContext);
    }

    static async Task<JsonDocument> RenderNextPageAsync(TestInfrastructure.TestContext test)
    {
        PrepareNextRequest(test);
        return await TestHelpers.ExecuteJsonAsync(test, TestHelpers.FreshInertia(test).Render("Users/Create", new { }));
    }

    static void RunFilter(TestInfrastructure.TestContext test, ModelStateDictionary modelState, IActionResult result)
    {
        var filter = (IResultFilter)FilterFactories(test).Single().CreateInstance(test.Services);
        var actionContext = new ActionContext(test.HttpContext, new RouteData(), new ActionDescriptor(), modelState);
        filter.OnResultExecuting(new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, new object()));
    }

    static IEnumerable<TypeFilterAttribute> FilterFactories(TestInfrastructure.TestContext test)
        => test.GetRequiredService<IOptions<MvcOptions>>().Value.Filters
            .OfType<TypeFilterAttribute>()
            .Where(filter => filter.ImplementationType.Name == "InertiaValidationErrorsFilter");

    static ModelStateDictionary Invalid(string key, params string[] messages)
    {
        var modelState = new ModelStateDictionary();
        foreach (var message in messages)
            modelState.AddModelError(key, message);
        return modelState;
    }

    static JsonElement Errors(JsonDocument document)
        => document.RootElement.GetProperty("props").GetProperty("errors");

    static void AssertNoErrors(JsonDocument document)
        => Assert.Empty(Errors(document).EnumerateObject());

    private sealed class TestInertiaController : InertiaController
    {
    }
}
