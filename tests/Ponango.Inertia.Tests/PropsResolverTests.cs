using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Prop resolution and metadata, following the reference adapter's PropsResolver.
/// </summary>
public class PropsResolverTests
{
    // ----- Once props -----

    [Fact]
    public async Task Except_once_header_skips_value_but_keeps_onceProps_metadata()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";
        var calls = 0;

        using var document = await Render(test, new { plans = Inertia.Once(() => ++calls) });

        Assert.False(Props(document).TryGetProperty("plans", out _));
        Assert.Equal("plans", OnceEntry(document, "plans").GetProperty("prop").GetString());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Partial_reload_resolves_once_prop_even_if_listed_in_except_once()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "plans");
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";

        using var document = await Render(test, new { plans = Inertia.Once(() => "pro") });

        Assert.Equal("pro", Props(document).GetProperty("plans").GetString());
        Assert.True(document.RootElement.GetProperty("onceProps").TryGetProperty("plans", out _));
    }

    [Fact]
    public async Task Fresh_once_prop_is_resolved_even_when_client_has_it()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";

        using var document = await Render(test, new { plans = Inertia.Once(() => "new").Fresh() });

        Assert.Equal("new", Props(document).GetProperty("plans").GetString());
    }

    [Fact]
    public async Task Once_custom_key_is_used_for_metadata_and_except_matching()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using (var first = await Render(test, new { memberRoles = Inertia.Once(() => new[] { "admin" }).As("roles") }))
        {
            Assert.True(Props(first).TryGetProperty("memberRoles", out _));
            Assert.Equal("memberRoles", OnceEntry(first, "roles").GetProperty("prop").GetString());
            Assert.False(first.RootElement.GetProperty("onceProps").TryGetProperty("memberRoles", out _));
        }

        TestHelpers.ResetResponse(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "roles";

        using var second = await Render(test, new { availableRoles = Inertia.Once(() => new[] { "admin" }).As("roles") }, fresh: true);
        Assert.False(Props(second).TryGetProperty("availableRoles", out _));
        Assert.Equal("availableRoles", OnceEntry(second, "roles").GetProperty("prop").GetString());
    }

    [Fact]
    public async Task Once_until_datetime_sets_expiresAt_in_milliseconds()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var expiresAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        using var document = await Render(test, new { rates = Inertia.Once(() => 1).Until(expiresAt) });

        Assert.Equal(expiresAt.ToUnixTimeMilliseconds(), OnceEntry(document, "rates").GetProperty("expiresAt").GetInt64());
    }

    [Fact]
    public async Task Html_visit_ignores_except_once_header()
    {
        var executor = new TestHelpers.CapturingViewResultExecutor();
        using var test = TestInfrastructure.CreateContext(configureServices: services =>
            services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<Microsoft.AspNetCore.Mvc.ViewResult>>(executor));
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";

        await test.GetRequiredService<InertiaContext>()
            .Render("Page", new { plans = Inertia.Once(() => "pro") })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        var props = (IDictionary<string, object?>)executor.Models.Single()!.Props;
        Assert.Equal("pro", props["plans"]);
    }

    [Fact]
    public async Task Shared_once_prop_via_ShareOnce_is_cached_like_page_once_props()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "countries";
        var inertia = test.GetRequiredService<InertiaContext>();
        var calls = 0;
        inertia.ShareOnce("countries", () => ++calls).Until(TimeSpan.FromDays(1));

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { }));

        Assert.Equal(0, calls);
        Assert.False(Props(document).TryGetProperty("countries", out _));
        Assert.Equal(JsonValueKind.Number, OnceEntry(document, "countries").GetProperty("expiresAt").ValueKind);
    }

    // ----- Deferred composition -----

    [Fact]
    public async Task Deferred_merge_prop_announces_deferred_and_merge_metadata_on_full_visit()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new { results = Inertia.Defer(() => new { data = 1 }).DeepMerge() });

        Assert.False(Props(document).TryGetProperty("results", out _));
        Assert.Equal(new[] { "results" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));
        Assert.Equal(new[] { "results" }, TestHelpers.Strings(document.RootElement.GetProperty("deepMergeProps")));
    }

    [Fact]
    public async Task Deferred_merge_prop_emits_merge_metadata_when_loaded()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "feed");

        using var document = await Render(test, new { feed = Inertia.Defer(() => new[] { 1, 2 }).Merge().MatchingOn("id") });

        Assert.Equal(2, Props(document).GetProperty("feed").GetArrayLength());
        Assert.Equal(new[] { "feed" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.Equal(new[] { "feed.id" }, TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
        Assert.False(document.RootElement.TryGetProperty("deferredProps", out _));
    }

    [Fact]
    public async Task Deferred_once_prop_already_loaded_is_not_announced_as_deferred_but_keeps_once_metadata()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "stats";

        using var document = await Render(test, new
        {
            stats = Inertia.Defer(() => 1).Once(),
            other = Inertia.Defer(() => 2)
        });

        Assert.Equal(new[] { "other" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));
        Assert.True(document.RootElement.GetProperty("onceProps").TryGetProperty("stats", out _));
    }

    [Fact]
    public async Task Optional_once_prop_emits_once_metadata_on_full_visit_without_resolving()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var calls = 0;

        using var document = await Render(test, new { categories = Inertia.Optional(() => ++calls).Once() });

        Assert.Equal(0, calls);
        Assert.False(Props(document).TryGetProperty("categories", out _));
        Assert.True(document.RootElement.GetProperty("onceProps").TryGetProperty("categories", out _));
    }

    [Fact]
    public async Task Merge_once_prop_emits_both_metadata()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new { activity = Inertia.Merge(() => new[] { 1 }).Once() });

        Assert.Equal(new[] { "activity" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.True(document.RootElement.GetProperty("onceProps").TryGetProperty("activity", out _));
    }

    // ----- Rescue -----

    [Fact]
    public async Task Rescued_deferred_prop_is_omitted_and_listed_in_rescuedProps()
    {
        var logs = new CapturingLoggerProvider();
        using var test = TestInfrastructure.CreateContext(configureServices: services =>
            services.AddSingleton<ILoggerProvider>(logs));
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "permissions,teams");

        using var document = await Render(test, new
        {
            permissions = Inertia.Defer(() => throw new InvalidOperationException("boom"), rescue: true),
            teams = Inertia.Defer(() => new[] { "a" })
        });

        Assert.Equal(200, test.HttpContext.Response.StatusCode);
        Assert.False(Props(document).TryGetProperty("permissions", out _));
        Assert.True(Props(document).TryGetProperty("teams", out _));
        Assert.Equal(new[] { "permissions" }, TestHelpers.Strings(document.RootElement.GetProperty("rescuedProps")));

        var entry = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("permissions", entry.Message);
    }

    [Fact]
    public async Task Rescue_fluent_method_enables_rescue()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "permissions");

        using var document = await Render(test, new
        {
            permissions = Inertia.Defer(() => throw new InvalidOperationException("boom")).Rescue()
        });

        Assert.Equal(new[] { "permissions" }, TestHelpers.Strings(document.RootElement.GetProperty("rescuedProps")));
    }

    [Fact]
    public async Task Unrescued_deferred_prop_exception_propagates()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "permissions");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Render(test, new
        {
            permissions = Inertia.Defer(() => throw new InvalidOperationException("boom"))
        }));
    }

    // ----- Merge paths -----

    [Fact]
    public async Task Merge_append_at_path_emits_nested_label()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new
        {
            users = Inertia.Merge(() => new { data = new[] { 1 }, total = 1 }).Append("data", matchOn: "id")
        });

        Assert.Equal(new[] { "users.data" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.Equal(new[] { "users.data.id" }, TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
    }

    [Fact]
    public async Task Merge_append_multiple_paths_with_match_fields()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new
        {
            dashboard = Inertia.Merge(() => new { }).Append(new Dictionary<string, string?>
            {
                ["users.data"] = "id",
                ["messages"] = "uuid",
                ["notes"] = null
            })
        });

        Assert.Equal(
            new[] { "dashboard.users.data", "dashboard.messages", "dashboard.notes" },
            TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.Equal(
            new[] { "dashboard.users.data.id", "dashboard.messages.uuid" },
            TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
    }

    [Fact]
    public async Task Merge_prepend_and_append_at_paths()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new
        {
            forum = Inertia.Merge(() => new { }).Append("posts").Prepend("announcements")
        });

        Assert.Equal(new[] { "forum.posts" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.Equal(new[] { "forum.announcements" }, TestHelpers.Strings(document.RootElement.GetProperty("prependProps")));
    }

    [Fact]
    public async Task Deep_merge_factory_with_match_on_nested_field()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new { chat = Inertia.DeepMerge(() => new { }).MatchingOn("messages.id") });

        Assert.Equal(new[] { "chat" }, TestHelpers.Strings(document.RootElement.GetProperty("deepMergeProps")));
        Assert.Equal(new[] { "chat.messages.id" }, TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
    }

    [Fact]
    public async Task Reset_header_suppresses_merge_metadata_but_returns_value()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "results");
        test.HttpContext.Request.Headers["X-Inertia-Reset"] = "results";

        using var document = await Render(test, new { results = Inertia.Merge(() => new[] { 1 }) });

        Assert.True(Props(document).TryGetProperty("results", out _));
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
    }

    [Fact]
    public async Task Merge_metadata_omitted_for_partial_excluded_prop()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", except: "feed");

        using var document = await Render(test, new { feed = Inertia.Merge(() => new[] { 1 }), other = 1 });

        Assert.False(Props(document).TryGetProperty("feed", out _));
        Assert.False(document.RootElement.TryGetProperty("mergeProps", out _));
    }

    [Fact]
    public async Task Plain_merge_prop_ignores_infinite_scroll_merge_intent_header()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "feed");
        test.HttpContext.Request.Headers["X-Inertia-Infinite-Scroll-Merge-Intent"] = "prepend";

        using var document = await Render(test, new { feed = Inertia.Merge(() => new[] { 1 }) });

        Assert.Equal(new[] { "feed" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.False(document.RootElement.TryGetProperty("prependProps", out _));
    }

    // ----- Unwrapping and ordering -----

    [Fact]
    public async Task Callback_returning_a_prop_type_is_unwrapped()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new
        {
            feed = Inertia.Always(() => Inertia.Merge(() => new[] { 1 }))
        });

        Assert.Equal(1, Props(document).GetProperty("feed").GetArrayLength());
        Assert.Equal(new[] { "feed" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
    }

    [Fact]
    public async Task Page_props_override_shared_props_and_shared_props_come_first()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", "shared-auth");
        inertia.Share("title", "shared-title");

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { title = "page-title", body = 1 }));

        var props = Props(document);
        Assert.Equal("page-title", props.GetProperty("title").GetString());
        Assert.Equal(new[] { "auth", "title", "body", "errors" }, props.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
    }

    // ----- helpers -----

    static async Task<JsonDocument> Render(TestInfrastructure.TestContext test, object props, bool fresh = false)
    {
        var inertia = fresh ? TestHelpers.FreshInertia(test) : test.GetRequiredService<InertiaContext>();
        return await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", props));
    }

    static JsonElement Props(JsonDocument document) => document.RootElement.GetProperty("props");

    static JsonElement OnceEntry(JsonDocument document, string key)
        => document.RootElement.GetProperty("onceProps").GetProperty(key);

    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger : ILogger
        {
            private readonly CapturingLoggerProvider _provider;

            public Logger(CapturingLoggerProvider provider) => _provider = provider;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _provider.Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
