using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// An Inertia request answered with an empty 200 is redirected back, as in inertia-laravel's
/// <c>Middleware::onEmptyResponse</c> (tightened in 3.5.2: a body of "0" or a streamed response is not empty).
/// </summary>
public class EmptyResponseTests
{
    [Fact]
    public async Task Empty_response_to_inertia_post_redirects_back_with_303()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users/create?step=2");

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/users/create?step=2", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Empty_response_to_inertia_get_redirects_back_with_302()
    {
        using var test = Start(HttpMethods.Get, referer: "https://app.test/users");

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status302Found, test.HttpContext.Response.StatusCode);
        Assert.Equal("/users", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Empty_content_result_counts_as_empty()
    {
        using var test = Start(HttpMethods.Put, referer: "https://app.test/users/1/edit");

        await Run(test, context => new ContentResult { Content = "" }
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(context)));

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/users/1/edit", test.HttpContext.Response.Headers.Location.ToString());
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task Without_referer_redirects_to_the_app_root()
    {
        using var test = Start(HttpMethods.Post);

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Without_referer_redirects_to_the_path_base()
    {
        using var test = Start(HttpMethods.Post);
        test.HttpContext.Request.PathBase = "/admin";

        await Run(test, Ok);

        Assert.Equal("/admin/", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task External_referer_is_ignored()
    {
        using var test = Start(HttpMethods.Post, referer: "https://evil.test/phish");

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        Assert.Equal("/", test.HttpContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Body_of_zero_is_not_empty()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, context => context.Response.WriteAsync("0"));

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.Equal("0", await ReadBody(test));
    }

    [Fact]
    public async Task Body_written_through_the_pipe_writer_is_not_empty()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, async context =>
        {
            await context.Response.BodyWriter.WriteAsync("ok"u8.ToArray());
            await context.Response.BodyWriter.FlushAsync();
        });

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.Equal("ok", await ReadBody(test));
    }

    [Fact]
    public async Task Started_streamed_response_is_not_empty()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, context => context.Response.StartAsync());

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Inertia_page_response_is_not_redirected()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, context => context.RequestServices.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new { })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(context)));

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        using var document = await TestInfrastructure.ReadJsonAsync(test.HttpContext.Response);
        Assert.Equal("Users/Index", document.RootElement.GetProperty("component").GetString());
    }

    [Fact]
    public async Task No_content_204_is_left_alone()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status204NoContent, test.HttpContext.Response.StatusCode);
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Non_inertia_request_is_left_alone()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = HttpMethods.Post;
        test.HttpContext.Request.Headers.Referer = "https://app.test/users";

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Option_off_leaves_empty_responses_alone()
    {
        using var test = TestInfrastructure.CreateContext(options => options.RedirectEmptyResponsesBack = false);
        test.HttpContext.Request.Method = HttpMethods.Post;
        test.HttpContext.Request.Headers.Referer = "https://app.test/users";
        TestHelpers.AsInertia(test.HttpContext);

        await Run(test, Ok);

        Assert.Equal(StatusCodes.Status200OK, test.HttpContext.Response.StatusCode);
        Assert.False(test.HttpContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Flash_data_survives_the_redirect_back()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");

        await Run(test, context =>
        {
            context.RequestServices.GetRequiredService<InertiaContext>().Flash("message", "Saved");
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status303SeeOther, test.HttpContext.Response.StatusCode);
        var tempData = test.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(test.HttpContext);
        Assert.Contains("__inertia_flash_message", tempData.Keys);
    }

    [Fact]
    public async Task Original_body_feature_is_restored_after_the_request()
    {
        using var test = Start(HttpMethods.Post, referer: "https://app.test/users");
        var body = test.HttpContext.Response.Body;

        await Run(test, Ok);

        Assert.Same(body, test.HttpContext.Response.Body);
    }

    static TestInfrastructure.TestContext Start(string method, string? referer = null)
    {
        var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Method = method;
        TestHelpers.AsInertia(test.HttpContext);
        if (referer != null)
            test.HttpContext.Request.Headers.Referer = referer;
        return test;
    }

    static Task Ok(HttpContext context)
        => new OkResult().ExecuteResultAsync(TestInfrastructure.CreateActionContext(context));

    static Task Run(TestInfrastructure.TestContext test, RequestDelegate action)
        => new InertiaMiddleware(action).InvokeAsync(test.HttpContext);

    static async Task<string> ReadBody(TestInfrastructure.TestContext test)
    {
        test.HttpContext.Response.Body.Position = 0;
        return await new StreamReader(test.HttpContext.Response.Body).ReadToEndAsync();
    }
}
