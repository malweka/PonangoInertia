using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Requests through a real app (routing, MVC filters, cookie TempData, the Inertia middleware), for behavior that
/// depends on the order those run in.
/// </summary>
public class MvcPipelineTests
{
    [Fact]
    public async Task Invalid_model_state_with_ok_result_reaches_the_page_after_the_redirect_back()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var post = InertiaRequest(HttpMethod.Post, "/pipeline/users");
        post.Headers.Referrer = new Uri("http://localhost/pipeline/users/create");
        using var postResponse = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.SeeOther, postResponse.StatusCode);
        Assert.Equal("/pipeline/users/create", postResponse.Headers.Location?.OriginalString);

        using var get = InertiaRequest(HttpMethod.Get, "/pipeline/users/create");
        CarryCookies(postResponse, get);
        using var getResponse = await client.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Email is required",
            document.RootElement.GetProperty("props").GetProperty("errors").GetProperty("email").GetString());
    }

    static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews()
            .ConfigureApplicationPartManager(parts =>
            {
                // Only this controller: the test assembly has others that are not meant to be routed.
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(PipelineUsersController)));
            });
        builder.Services.AddInertia();
        builder.Services.AddSingleton<IAssetVersionProvider>(new FixedVersion());

        var app = builder.Build();
        app.UseRouting();
        app.UseInertia();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    static HttpRequestMessage InertiaRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Inertia", "true");
        return request;
    }

    static void CarryCookies(HttpResponseMessage from, HttpRequestMessage to)
    {
        if (!from.Headers.TryGetValues("Set-Cookie", out var cookies))
            return;

        to.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => cookie.Split(';')[0])));
    }

    private sealed class FixedVersion : IAssetVersionProvider
    {
        public string GetAssetVersion() => "test-version";
    }

    private sealed class SingleControllerFeatureProvider(Type controller)
        : Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
    {
        public void PopulateFeature(
            IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts,
            Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature)
            => feature.Controllers.Add(controller.GetTypeInfo());
    }
}

public sealed class PipelineUsersController : Controller
{
    [HttpPost("/pipeline/users")]
    public IActionResult Store()
    {
        ModelState.AddModelError("email", "Email is required");
        return Ok();
    }

    [HttpGet("/pipeline/users/create")]
    public IActionResult Create([FromServices] InertiaContext inertia) => inertia.Render("Users/Create", new { });
}
