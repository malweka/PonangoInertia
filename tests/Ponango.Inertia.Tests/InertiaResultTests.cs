using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ponango.Inertia.Tests;

public class InertiaResultTests
{
    [Fact]
    public async Task Flash_is_merged_with_existing_shared_flash_values()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("flash", new Dictionary<string, object?> { ["existing"] = "keep" });
        inertia.Flash("success", "created");

        var result = inertia.Inertia("Inertia", new { }, "Users/Index");
        result.Url = "/users";

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var flash = document.RootElement.GetProperty("props").GetProperty("flash");

        Assert.Equal("keep", flash.GetProperty("existing").GetString());
        Assert.Equal("created", flash.GetProperty("success").GetString());
    }

    [Fact]
    public async Task Partial_except_only_resolves_optional_and_deferred_props_not_excluded()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Version"] = "test-version";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Dashboard";
        httpContext.Request.Headers["X-Inertia-Partial-Except"] = "users";

        var inertia = test.GetRequiredService<InertiaContext>();
        var optionalCalls = 0;
        var deferredCalls = 0;

        var result = inertia.Inertia("Inertia", new { }, "Dashboard");
        result.Url = "/dashboard";
        result.Props["users"] = new[] { "alice" };
        result.Props["stats"] = new OptionalProp(() =>
        {
            optionalCalls++;
            return new { total = 10 };
        });
        result.Props["analytics"] = new DeferredProp(() =>
        {
            deferredCalls++;
            return new { visits = 20 };
        });

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var props = document.RootElement.GetProperty("props");

        // On a partial reload the only gate is the only/except filter, as in the reference adapter.
        Assert.False(props.TryGetProperty("users", out _));
        Assert.True(props.TryGetProperty("stats", out _));
        Assert.True(props.TryGetProperty("analytics", out _));
        Assert.Equal(1, optionalCalls);
        Assert.Equal(1, deferredCalls);
    }

    [Fact]
    public async Task Preserve_fragment_flag_is_written_to_page_object()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Inertia("Inertia", new { }, "Users/Index");
        result.Url = "/users#details";
        result.WithPreserveFragment();

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.True(document.RootElement.GetProperty("preserveFragment").GetBoolean());
    }

    [Fact]
    public async Task Scroll_props_are_populated_for_merge_props()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";

        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Inertia("Inertia", new { }, "Posts/Index");
        result.Url = "/posts";
        result.Props["posts"] = new MergeProp(
            () => new[] { "first" },
            MergeMode.Append)
            .WithScroll(currentPage: 1, previousPage: null, nextPage: 2, pageName: "page");

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        var scrollProps = document.RootElement.GetProperty("scrollProps").GetProperty("posts");

        Assert.Equal("page", scrollProps.GetProperty("pageName").GetString());
        Assert.Equal(1, scrollProps.GetProperty("currentPage").GetInt32());
        Assert.Equal(2, scrollProps.GetProperty("nextPage").GetInt32());
        Assert.Contains(
            document.RootElement.GetProperty("mergeProps").EnumerateArray().Select(x => x.GetString()),
            value => value == "posts");
    }

    [Fact]
    public async Task Merge_intent_header_can_switch_append_to_prepend()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Version"] = "test-version";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Posts/Index";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "posts";
        httpContext.Request.Headers["X-Inertia-Infinite-Scroll-Merge-Intent"] = "prepend";

        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Inertia("Inertia", new { }, "Posts/Index");
        result.Url = "/posts";
        result.Props["posts"] = new MergeProp(() => new[] { "newest" }, MergeMode.Append);

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
        Assert.Contains(
            document.RootElement.GetProperty("prependProps").EnumerateArray().Select(x => x.GetString()),
            value => value == "posts");
    }

    [Fact]
    public async Task Reset_header_clears_merge_metadata_for_matching_props()
    {
        using var test = TestInfrastructure.CreateContext();
        var httpContext = test.HttpContext;
        httpContext.Request.Headers["X-Inertia"] = "true";
        httpContext.Request.Headers["X-Inertia-Version"] = "test-version";
        httpContext.Request.Headers["X-Inertia-Partial-Component"] = "Posts/Index";
        httpContext.Request.Headers["X-Inertia-Partial-Data"] = "posts";
        httpContext.Request.Headers["X-Inertia-Reset"] = "posts";

        var inertia = test.GetRequiredService<InertiaContext>();
        var result = inertia.Inertia("Inertia", new { }, "Posts/Index");
        result.Url = "/posts";
        result.Props["posts"] = new MergeProp(() => new[] { "page-2" }, MergeMode.Append)
            .WithScroll(currentPage: 2, previousPage: 1, nextPage: 3);

        await result.ExecuteResultAsync(TestInfrastructure.CreateActionContext(httpContext));

        using var document = await TestInfrastructure.ReadJsonAsync(httpContext.Response);
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
        Assert.True(document.RootElement.TryGetProperty("scrollProps", out var scrollProps));
        Assert.True(scrollProps.TryGetProperty("posts", out _));
    }
}
