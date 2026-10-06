using System.Text.Json;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Infinite scroll props (https://inertiajs.com/docs/v3/data-props/infinite-scroll).
/// </summary>
public class ScrollPropTests
{
    static readonly object PostsPage = new { data = new[] { new { id = 1 }, new { id = 2 } }, total = 40 };

    [Fact]
    public async Task Scroll_prop_full_visit_labels_inner_wrapper_and_emits_metadata()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, Inertia.Scroll(() => PostsPage, ScrollMetadata.ForPage(1, null, 2)));

        Assert.Equal(2, document.RootElement.GetProperty("props").GetProperty("posts").GetProperty("data").GetArrayLength());
        Assert.Equal(new[] { "posts.data" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));

        var scroll = document.RootElement.GetProperty("scrollProps").GetProperty("posts");
        Assert.Equal("page", scroll.GetProperty("pageName").GetString());
        Assert.Equal(JsonValueKind.Null, scroll.GetProperty("previousPage").ValueKind);
        Assert.Equal(2, scroll.GetProperty("nextPage").GetInt32());
        Assert.Equal(1, scroll.GetProperty("currentPage").GetInt32());
        Assert.False(scroll.GetProperty("reset").GetBoolean());
    }

    [Fact]
    public async Task Scroll_prop_reset_request_sets_reset_true_and_drops_merge_label()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "posts");
        test.HttpContext.Request.Headers["X-Inertia-Reset"] = "posts";

        using var document = await Render(test, Inertia.Scroll(() => PostsPage, ScrollMetadata.ForPage(1, null, 2)));

        Assert.True(document.RootElement.GetProperty("scrollProps").GetProperty("posts").GetProperty("reset").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
        Assert.True(document.RootElement.GetProperty("props").TryGetProperty("posts", out _));
    }

    [Fact]
    public async Task Scroll_prop_prepend_intent_uses_prependProps()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "posts");
        test.HttpContext.Request.Headers["X-Inertia-Infinite-Scroll-Merge-Intent"] = "prepend";

        using var document = await Render(test, Inertia.Scroll(() => PostsPage, ScrollMetadata.ForPage(3, 2, 4)).MatchingOn("id"));

        Assert.Equal(new[] { "posts.data" }, TestHelpers.Strings(document.RootElement.GetProperty("prependProps")));
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
        Assert.Equal(new[] { "posts.data.id" }, TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
    }

    [Fact]
    public async Task Scroll_prop_cursor_metadata_emits_strings()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, Inertia.Scroll(
            () => PostsPage,
            ScrollMetadata.ForCursor("eyJpZCI6MTB9", null, "eyJpZCI6MjB9")));

        var scroll = document.RootElement.GetProperty("scrollProps").GetProperty("posts");
        Assert.Equal("cursor", scroll.GetProperty("pageName").GetString());
        Assert.Equal("eyJpZCI6MTB9", scroll.GetProperty("currentPage").GetString());
        Assert.Equal("eyJpZCI6MjB9", scroll.GetProperty("nextPage").GetString());
        Assert.Equal(JsonValueKind.Null, scroll.GetProperty("previousPage").ValueKind);
    }

    [Fact]
    public async Task Scroll_metadata_callback_receives_the_resolved_value_once()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var calls = 0;
        object? seen = null;

        using var document = await Render(test, Inertia.Scroll(
            () => { calls++; return new Page(new[] { 1, 2 }, 5); },
            value =>
            {
                seen = value;
                var page = (Page)value;
                return ScrollMetadata.ForPage(page.CurrentPage, page.CurrentPage - 1, page.CurrentPage + 1, "users");
            }));

        Assert.Equal(1, calls);
        Assert.IsType<Page>(seen);
        var scroll = document.RootElement.GetProperty("scrollProps").GetProperty("posts");
        Assert.Equal("users", scroll.GetProperty("pageName").GetString());
        Assert.Equal(5, scroll.GetProperty("currentPage").GetInt32());
    }

    [Fact]
    public async Task Deferred_scroll_prop_full_visit_announces_deferred_and_merge_without_scrollProps()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var calls = 0;

        using var document = await Render(test, Inertia.Scroll(() => { calls++; return PostsPage; }, ScrollMetadata.ForPage(1, null, 2)).Defer("feed"));

        Assert.Equal(0, calls);
        Assert.False(document.RootElement.GetProperty("props").TryGetProperty("posts", out _));
        Assert.Equal(new[] { "posts" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("feed")));
        Assert.Equal(new[] { "posts.data" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.False(document.RootElement.TryGetProperty("scrollProps", out _));
    }

    [Fact]
    public async Task Deferred_scroll_prop_resolves_with_scroll_metadata_when_requested()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "posts");

        using var document = await Render(test, Inertia.Scroll(() => PostsPage, ScrollMetadata.ForPage(1, null, 2)).Defer());

        Assert.True(document.RootElement.GetProperty("props").TryGetProperty("posts", out _));
        Assert.True(document.RootElement.GetProperty("scrollProps").TryGetProperty("posts", out _));
        Assert.False(document.RootElement.TryGetProperty("deferredProps", out _));
    }

    [Fact]
    public async Task Custom_wrapper_key_is_used_in_merge_label()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, Inertia.Scroll(
            () => new { items = new[] { 1 } },
            ScrollMetadata.ForPage(1, null, null),
            wrapper: "items"));

        Assert.Equal(new[] { "posts.items" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
    }

    [Fact]
    public async Task Legacy_WithScroll_emits_reset_and_explicit_nulls_and_keeps_root_label()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

#pragma warning disable CS0618 // legacy API coverage
        var prop = Inertia.Merge(() => new[] { 1 }).WithScroll(currentPage: 1, previousPage: null, nextPage: 2);
#pragma warning restore CS0618

        using var document = await Render(test, prop);

        Assert.Equal(new[] { "posts" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        var scroll = document.RootElement.GetProperty("scrollProps").GetProperty("posts");
        Assert.Equal(JsonValueKind.Null, scroll.GetProperty("previousPage").ValueKind);
        Assert.False(scroll.GetProperty("reset").GetBoolean());
    }

    static Task<JsonDocument> Render(TestInfrastructure.TestContext test, object posts)
    {
        var props = new Dictionary<string, object> { ["posts"] = posts };
        return TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>().Render("Page", props));
    }

    private sealed record Page(int[] Data, int CurrentPage);
}
