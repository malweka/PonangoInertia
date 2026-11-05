using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Live props (Inertia.js 3.11, inertia-laravel 3.7 <c>Inertia::live()</c> / <c>->live()</c>): the page object's
/// <c>liveProps</c> lists, per prop path, the channels and events that refresh the prop.
/// </summary>
public class LivePropTests
{
    // ----- Metadata shape -----

    [Fact]
    public async Task Live_prop_emits_its_listener_without_throttle()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(new { id = 1 }, "order.updated", LiveChannel.Private("orders.1"))
        });

        Assert.Equal(1, Props(document).GetProperty("order").GetProperty("id").GetInt32());
        var entry = LiveEntry(document, "order");
        Assert.False(entry.TryGetProperty("throttle", out _));
        Assert.Equal(
            """[{"channel":{"name":"orders.1","type":"private"},"events":["order.updated"]}]""",
            entry.GetProperty("listeners").GetRawText());
    }

    [Fact]
    public async Task Live_props_field_is_omitted_when_no_prop_is_live()
    {
        using var document = await RenderFull(new { order = new { id = 1 }, items = Inertia.Defer(() => 1) });

        Assert.False(document.RootElement.TryGetProperty("liveProps", out _));
    }

    [Fact]
    public async Task Throttle_is_sent_in_milliseconds_and_the_last_one_wins()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, "order.updated", "orders", TimeSpan.FromSeconds(2))
                .Live("order.cancelled", "orders", TimeSpan.FromMilliseconds(4000))
        });

        Assert.Equal(4000, LiveEntry(document, "order").GetProperty("throttle").GetInt64());
    }

    [Fact]
    public async Task Events_on_one_channel_collapse_into_one_listener_without_duplicates()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, new[] { "order.updated", "order.cancelled" }, LiveChannel.Private("orders.1"))
                .Live("order.updated", LiveChannel.Private("orders.1"))
        });

        var listeners = Listeners(document, "order");
        var listener = Assert.Single(listeners);
        Assert.Equal(new[] { "order.updated", "order.cancelled" }, TestHelpers.Strings(listener.GetProperty("events")));
    }

    [Fact]
    public async Task Each_channel_gets_its_own_listener_in_declaration_order()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, new[] { "order.updated" }, new LiveChannel[] { LiveChannel.Private("orders.1"), "dashboard" })
        });

        var listeners = Listeners(document, "order");
        Assert.Equal(2, listeners.Count);
        Assert.Equal("private", listeners[0].GetProperty("channel").GetProperty("type").GetString());
        Assert.Equal("public", listeners[1].GetProperty("channel").GetProperty("type").GetString());
        Assert.Equal("dashboard", listeners[1].GetProperty("channel").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_channel_applies_only_to_the_events_declared_with_it()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, "order.updated", LiveChannel.Private("orders.1"))
                .Live("stats.changed", "dashboard")
        });

        var listeners = Listeners(document, "order");
        Assert.Equal(new[] { "order.updated" }, TestHelpers.Strings(listeners[0].GetProperty("events")));
        Assert.Equal(new[] { "stats.changed" }, TestHelpers.Strings(listeners[1].GetProperty("events")));
    }

    [Theory]
    [InlineData(LiveChannelType.Public, "public")]
    [InlineData(LiveChannelType.Private, "private")]
    [InlineData(LiveChannelType.Presence, "presence")]
    [InlineData(LiveChannelType.EncryptedPrivate, "encrypted-private")]
    public async Task Every_channel_type_is_serialized(LiveChannelType type, string expected)
    {
        using var document = await RenderFull(new { order = Inertia.Live(1, "e", new LiveChannel("orders", type)) });

        Assert.Equal(expected, Listeners(document, "order")[0].GetProperty("channel").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("private-orders.1", LiveChannelType.Private, "orders.1")]
    [InlineData("presence-room.1", LiveChannelType.Presence, "room.1")]
    [InlineData("private-encrypted-vault", LiveChannelType.EncryptedPrivate, "vault")]
    [InlineData("private-orders.1", LiveChannelType.Public, "private-orders.1")]
    [InlineData("orders.1", LiveChannelType.Private, "orders.1")]
    public void Channel_prefix_matching_the_type_is_removed(string name, LiveChannelType type, string expected)
    {
        var channel = new LiveChannel(name, type);

        Assert.Equal(expected, channel.Name);
        Assert.Equal($"{channel.TypeName}:{expected}", channel.Key);
    }

    [Fact]
    public void A_bare_string_is_a_public_channel()
    {
        LiveChannel channel = "orders.1";

        Assert.Equal(LiveChannelType.Public, channel.Type);
        Assert.Equal("public:orders.1", channel.Key);
    }

    [Fact]
    public async Task Live_event_brings_its_own_channels()
    {
        using var document = await RenderFull(new { order = Inertia.Live(1, new OrderUpdated(7)) });

        var listener = Assert.Single(Listeners(document, "order"));
        Assert.Equal("orders.7", listener.GetProperty("channel").GetProperty("name").GetString());
        Assert.Equal("private", listener.GetProperty("channel").GetProperty("type").GetString());
        Assert.Equal(new[] { "order.updated" }, TestHelpers.Strings(listener.GetProperty("events")));
    }

    [Fact]
    public async Task A_given_channel_wins_over_the_event_channels()
    {
        using var document = await RenderFull(new { order = Inertia.Live(1, new OrderUpdated(7), "dashboard") });

        var listener = Assert.Single(Listeners(document, "order"));
        Assert.Equal("public:dashboard",
            $"{listener.GetProperty("channel").GetProperty("type").GetString()}:{listener.GetProperty("channel").GetProperty("name").GetString()}");
    }

    [Fact]
    public async Task Several_live_events_each_use_their_own_channels()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, new ILiveEvent[] { new OrderUpdated(7), new OrderShipped(7) })
        });

        var listeners = Listeners(document, "order");
        var listener = Assert.Single(listeners);
        Assert.Equal(new[] { "order.updated", "order.shipped" }, TestHelpers.Strings(listener.GetProperty("events")));
    }

    [Fact]
    public async Task A_lazy_value_is_evaluated()
    {
        var calls = 0;
        using var document = await RenderFull(new { order = Inertia.Live(() => ++calls, "order.updated", "orders") });

        Assert.Equal(1, calls);
        Assert.Equal(1, Props(document).GetProperty("order").GetInt32());
    }

    [Fact]
    public async Task A_live_prop_returned_from_a_closure_is_unwrapped()
    {
        using var document = await RenderFull(new
        {
            order = (Func<object>)(() => Inertia.Live(5, "order.updated", "orders"))
        });

        Assert.Equal(5, Props(document).GetProperty("order").GetInt32());
        Assert.Equal(JsonValueKind.Object, LiveEntry(document, "order").ValueKind);
    }

    // ----- Modifiers and errors -----

    [Fact]
    public async Task Supported_prop_types_can_be_live_and_keep_their_behavior()
    {
        using var document = await RenderFull(new
        {
            always = Inertia.Always(() => "a").Live("e", "c"),
            once = Inertia.Once(() => "o").Live("e", "c"),
            optional = Inertia.Optional(() => "x").Live("e", "c"),
            deferred = Inertia.Defer(() => "d").Live("e", "c")
        });

        var props = Props(document);
        Assert.Equal("a", props.GetProperty("always").GetString());
        Assert.Equal("o", props.GetProperty("once").GetString());
        Assert.False(props.TryGetProperty("optional", out _));
        Assert.False(props.TryGetProperty("deferred", out _));
        Assert.Contains("deferred", TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));

        var live = document.RootElement.GetProperty("liveProps");
        Assert.Equal(new[] { "always", "once", "optional", "deferred" }, live.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void A_merge_prop_cannot_be_live()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Inertia.Merge(() => 1).Live("e", "c"));
        Assert.Equal("Live updates are not supported on merge properties.", error.Message);
        Assert.Throws<InvalidOperationException>(() => Inertia.DeepMerge(() => 1).Live("e", "c"));
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Merge().Live("e", "c"));
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Append("items").Live(new OrderUpdated(1)));
    }

    [Fact]
    public void A_live_prop_cannot_be_made_mergeable()
    {
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Live("e", "c").Merge());
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Live("e", "c").DeepMerge());
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Live("e", "c").Prepend("items"));
        Assert.Throws<InvalidOperationException>(() => Inertia.Defer(() => 1).Live("e", "c").Append("items", "id"));
    }

    [Fact]
    public async Task A_live_prop_without_events_throws_when_the_page_is_built()
    {
        var prop = Inertia.Optional(() => 1).Live(Array.Empty<string>(), "orders");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderFull(new { prop }));
        Assert.Contains("at least one event", error.Message);
    }

    [Fact]
    public async Task A_string_event_without_channels_throws_when_the_page_is_built()
    {
        var prop = Inertia.Live(1, new[] { "order.updated" }, Array.Empty<LiveChannel>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderFull(new { prop }));
        Assert.Contains("[order.updated]", error.Message);
    }

    [Fact]
    public async Task A_live_event_without_channels_throws_when_the_page_is_built()
    {
        var prop = Inertia.Live(1, new ChannelLessEvent());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderFull(new { prop }));
        Assert.Contains("does not broadcast on any channels", error.Message);
    }

    [Fact]
    public async Task A_declaration_without_events_contributes_nothing()
    {
        using var document = await RenderFull(new
        {
            order = Inertia.Live(1, new OrderUpdated(1)).Live(Array.Empty<string>(), "ignored")
        });

        var listener = Assert.Single(Listeners(document, "order"));
        Assert.Equal("orders.1", listener.GetProperty("channel").GetProperty("name").GetString());
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new LiveChannel(" "));
        Assert.Throws<ArgumentException>(() => Inertia.Live(1, " ", "c"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Inertia.Live(1, "e", "c", TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentNullException>(() => Inertia.Live(1, "e", (LiveChannel)null!));
    }

    // ----- Resolver rules -----

    [Fact]
    public async Task Deferred_live_prop_is_announced_in_deferredProps_and_liveProps_on_a_full_visit()
    {
        using var document = await RenderFull(new { feed = Inertia.Defer(() => 1, "sidebar").Live("feed.updated", "feed") });

        Assert.False(Props(document).TryGetProperty("feed", out _));
        Assert.Equal(new[] { "feed" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("sidebar")));
        Assert.Equal(JsonValueKind.Object, LiveEntry(document, "feed").ValueKind);
    }

    [Fact]
    public async Task Once_live_prop_held_by_the_client_keeps_its_live_entry()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "plans";
        var calls = 0;

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Page", new { plans = Inertia.Once(() => ++calls).Live("plans.changed", "plans") }));

        Assert.Equal(0, calls);
        Assert.False(Props(document).TryGetProperty("plans", out _));
        Assert.Equal(JsonValueKind.Object, LiveEntry(document, "plans").ValueKind);
    }

    [Fact]
    public async Task Initial_html_visit_also_carries_liveProps()
    {
        using var test = TestInfrastructure.CreateContext(configureServices: services =>
            services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<Microsoft.AspNetCore.Mvc.ViewResult>>(
                new TestHelpers.CapturingViewResultExecutor()));
        var executor = (TestHelpers.CapturingViewResultExecutor)test.GetRequiredService<
            Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<Microsoft.AspNetCore.Mvc.ViewResult>>();

        await test.GetRequiredService<InertiaContext>()
            .Render("Page", new { order = Inertia.Live(1, "order.updated", "orders") })
            .ExecuteResultAsync(TestInfrastructure.CreateActionContext(test.HttpContext));

        Assert.True(executor.Models.Single()!.LiveProps!.ContainsKey("order"));
    }

    [Fact]
    public async Task Partial_reload_sends_the_entry_of_a_selected_live_prop()
    {
        using var document = await RenderPartial(only: "order", props: new
        {
            order = Inertia.Live(1, "order.updated", "orders"),
            status = Inertia.Live(2, "status.changed", "orders")
        });

        Assert.True(document.RootElement.GetProperty("liveProps").TryGetProperty("order", out _));
        Assert.False(document.RootElement.GetProperty("liveProps").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Partial_reload_except_drops_the_entry()
    {
        using var document = await RenderPartial(except: "order", props: new
        {
            order = Inertia.Live(1, "order.updated", "orders"),
            status = Inertia.Live(2, "status.changed", "orders")
        });

        Assert.False(document.RootElement.GetProperty("liveProps").TryGetProperty("order", out _));
        Assert.True(document.RootElement.GetProperty("liveProps").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Live_always_prop_keeps_its_entry_on_an_unrelated_partial_reload()
    {
        using var document = await RenderPartial(only: "metrics", props: new
        {
            order = Inertia.Always(() => "bar").Live("order.updated", "orders"),
            metrics = Inertia.Live("baz", "metrics.changed", "metrics"),
            status = Inertia.Live("ready", "status.changed", "status")
        });

        var live = document.RootElement.GetProperty("liveProps");
        Assert.True(live.TryGetProperty("order", out _));
        Assert.True(live.TryGetProperty("metrics", out _));
        Assert.False(live.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Live_child_of_an_always_parent_keeps_its_entry_when_excepted()
    {
        using var document = await RenderPartial(except: "order", props: new
        {
            order = Inertia.Always(() => new { total = Inertia.Live(() => 100, "order.updated", "orders") })
        });

        Assert.Equal(100, Props(document).GetProperty("order").GetProperty("total").GetInt32());
        Assert.True(document.RootElement.GetProperty("liveProps").TryGetProperty("order.total", out _));
    }

    [Fact]
    public async Task Nested_live_props_are_keyed_by_their_dot_path()
    {
        using var document = await RenderFull(new
        {
            auth = new { notifications = Inertia.Live(() => 3, "notification.created", LiveChannel.Private("users.1")) }
        });

        Assert.Equal(3, Props(document).GetProperty("auth").GetProperty("notifications").GetInt32());
        Assert.True(document.RootElement.GetProperty("liveProps").TryGetProperty("auth.notifications", out _));
    }

    [Fact]
    public async Task Nested_live_prop_is_sent_when_its_parent_is_selected()
    {
        using var document = await RenderPartial(only: "auth", props: new
        {
            auth = new { notifications = Inertia.Live(() => 3, "notification.created", "users") },
            other = Inertia.Live(1, "other", "other")
        });

        var live = document.RootElement.GetProperty("liveProps");
        Assert.True(live.TryGetProperty("auth.notifications", out _));
        Assert.False(live.TryGetProperty("other", out _));
    }

    [Fact]
    public async Task Rescued_live_prop_announces_on_the_full_visit_but_not_on_its_failed_reload()
    {
        using (var full = await RenderFull(new
               {
                   orders = Inertia.Defer(() => throw new InvalidOperationException("boom"), rescue: true).Live("e", "c")
               }))
        {
            Assert.True(full.RootElement.GetProperty("liveProps").TryGetProperty("orders", out _));
        }

        using var partial = await RenderPartial(only: "orders", props: new
        {
            orders = Inertia.Defer(() => throw new InvalidOperationException("boom"), rescue: true).Live("e", "c")
        });

        Assert.Contains("orders", TestHelpers.Strings(partial.RootElement.GetProperty("rescuedProps")));
        Assert.False(partial.RootElement.TryGetProperty("liveProps", out _));
    }

    [Fact]
    public async Task Merge_and_live_props_render_together()
    {
        using var document = await RenderFull(new
        {
            feed = Inertia.Merge(() => new[] { 1 }),
            order = Inertia.Live(1, "order.updated", "orders")
        });

        Assert.Equal(new[] { "feed" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.True(document.RootElement.GetProperty("liveProps").TryGetProperty("order", out _));
    }

    // ----- Socket id -----

    [Fact]
    public void Socket_id_header_is_exposed()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Headers["X-Socket-Id"] = "123.456";

        Assert.Equal("123.456", test.GetRequiredService<InertiaContext>().SocketId);
        Assert.Equal("123.456", test.GetRequiredService<InertiaContext>().Headers.SocketId);
    }

    [Fact]
    public void Socket_id_is_null_without_the_header()
    {
        using var test = TestInfrastructure.CreateContext();

        Assert.Null(test.GetRequiredService<InertiaContext>().SocketId);
    }

    // ----- Helpers -----

    static async Task<JsonDocument> RenderFull(object props)
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        return await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>().Render("Page", props));
    }

    static async Task<JsonDocument> RenderPartial(object props, string? only = null, string? except = null)
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: only, except: except);
        return await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>().Render("Page", props));
    }

    static JsonElement Props(JsonDocument document) => document.RootElement.GetProperty("props");

    static JsonElement LiveEntry(JsonDocument document, string path)
        => document.RootElement.GetProperty("liveProps").GetProperty(path);

    static IReadOnlyList<JsonElement> Listeners(JsonDocument document, string path)
        => LiveEntry(document, path).GetProperty("listeners").EnumerateArray().ToList();

    internal sealed class OrderUpdated : ILiveEvent
    {
        private readonly int _id;

        public OrderUpdated(int id) => _id = id;

        public string EventName => "order.updated";

        public IEnumerable<LiveChannel> Channels => new[] { LiveChannel.Private($"orders.{_id}") };
    }

    private sealed class OrderShipped : ILiveEvent
    {
        private readonly int _id;

        public OrderShipped(int id) => _id = id;

        public string EventName => "order.shipped";

        public IEnumerable<LiveChannel> Channels => new[] { LiveChannel.Private($"orders.{_id}") };
    }

    private sealed class ChannelLessEvent : ILiveEvent
    {
        public string EventName => "nothing";

        public IEnumerable<LiveChannel> Channels => Array.Empty<LiveChannel>();
    }
}
