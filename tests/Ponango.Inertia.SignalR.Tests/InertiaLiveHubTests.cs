using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ponango.Inertia.SignalR.Tests;

/// <summary>
/// The hub contract shared with the <c>@ponango/inertia-signalr</c> transport: <c>Subscribe(type, name)</c>,
/// <c>Unsubscribe(type, name)</c>, and events sent to <c>inertia(channelKey, payload)</c>.
/// </summary>
public class InertiaLiveHubTests
{
    // To show that a client did not receive an event, each test broadcasts a marker afterwards on a channel the client
    // is subscribed to, and checks that the marker is the next event it gets.
    const string MarkerChannel = "marker";

    [Fact]
    public async Task Public_channel_subscriber_receives_events_with_props()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var client = await app.ConnectAsync();
        await client.SubscribeAsync("public", "dashboard");

        await app.Broadcaster.BroadcastAsync("stats.changed", "dashboard", new { stats = new { orders = 3 } });

        var (channelKey, payload) = await client.NextAsync();
        Assert.Equal("public:dashboard", channelKey);
        Assert.Equal(
            """{"__inertia":{"props":{"stats":{"orders":3}},"event":"stats.changed"}}""",
            payload.GetRawText());
    }

    [Fact]
    public async Task Event_without_props_carries_only_the_event_name()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var client = await app.ConnectAsync();
        await client.SubscribeAsync("public", "dashboard");

        await app.Broadcaster.BroadcastAsync("stats.changed", "dashboard");

        var (_, payload) = await client.NextAsync();
        Assert.Equal("""{"__inertia":{"props":{},"event":"stats.changed"}}""", payload.GetRawText());
    }

    [Fact]
    public async Task Unsubscribed_client_no_longer_receives_events()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var client = await app.ConnectAsync();
        await client.SubscribeAsync("public", "dashboard");
        await client.SubscribeAsync("public", MarkerChannel);
        await client.UnsubscribeAsync("public", "dashboard");

        await app.Broadcaster.BroadcastAsync("stats.changed", "dashboard");
        await app.Broadcaster.BroadcastAsync("marker", MarkerChannel);

        Assert.Equal("public:marker", (await client.NextAsync()).ChannelKey);
    }

    [Fact]
    public async Task Unsubscribing_from_an_unknown_channel_is_ignored()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var client = await app.ConnectAsync();

        await client.UnsubscribeAsync("public", "never-joined");
        await client.UnsubscribeAsync("bogus", "x");
    }

    [Fact]
    public async Task Private_channel_allowed_by_its_rule_receives_events()
    {
        await using var app = await LiveHubFixture.StartAsync(OrdersRule);
        var client = await app.ConnectAsync("alice");

        await client.SubscribeAsync("private", "orders.1");
        await app.Broadcaster.BroadcastAsync("order.updated", LiveChannel.Private("orders.1"));

        Assert.Equal("private:orders.1", (await client.NextAsync()).ChannelKey);
    }

    // LiveChannel.Private("private-private-orders.1") is named "private-orders.1" after its one prefix is removed. The
    // hub must not remove another one, or it would authorize and join "orders.1" instead.
    [Fact]
    public async Task Channel_name_that_still_starts_with_its_prefix_is_kept_as_sent()
    {
        await using var app = await LiveHubFixture.StartAsync(options =>
        {
            options.Channel("orders.{id}", _ => false);
            options.Channel("private-orders.{id}", context => context.User.Identity?.Name == "alice");
        });
        var channel = LiveChannel.Private("private-private-orders.1");
        Assert.Equal("private-orders.1", channel.Name);

        var client = await app.ConnectAsync("alice");
        await client.SubscribeAsync("public", MarkerChannel);
        await client.SubscribeAsync(channel.TypeName, channel.Name);

        await app.Broadcaster.BroadcastAsync("order.updated", channel);
        Assert.Equal("private:private-orders.1", (await client.NextAsync()).ChannelKey);

        await client.UnsubscribeAsync(channel.TypeName, channel.Name);
        await app.Broadcaster.BroadcastAsync("order.updated", channel);
        await app.Broadcaster.BroadcastAsync("marker", MarkerChannel);
        Assert.Equal("public:marker", (await client.NextAsync()).ChannelKey);
    }

    [Theory]
    [InlineData("bob", "orders.1")]       // the rule says no
    [InlineData(null, "orders.1")]        // not signed in
    [InlineData("alice", "orders.2")]     // the captured id does not pass
    [InlineData("alice", "orders.1.items")] // no rule matches
    [InlineData("alice", "invoices.1")]   // no rule matches
    public async Task Private_channel_is_refused(string? user, string name)
    {
        await using var app = await LiveHubFixture.StartAsync(OrdersRule);
        var client = await app.ConnectAsync(user);

        var error = await Assert.ThrowsAsync<HubException>(() => client.SubscribeAsync("private", name));
        Assert.Contains("Unauthorized", error.Message);
    }

    [Fact]
    public async Task Refused_client_does_not_receive_events()
    {
        await using var app = await LiveHubFixture.StartAsync(OrdersRule);
        var client = await app.ConnectAsync("bob");
        await client.SubscribeAsync("public", MarkerChannel);
        await Assert.ThrowsAsync<HubException>(() => client.SubscribeAsync("private", "orders.1"));

        await app.Broadcaster.BroadcastAsync("order.updated", LiveChannel.Private("orders.1"));
        await app.Broadcaster.BroadcastAsync("marker", MarkerChannel);

        Assert.Equal("public:marker", (await client.NextAsync()).ChannelKey);
    }

    [Fact]
    public async Task Presence_channels_are_authorized_like_private_ones()
    {
        await using var app = await LiveHubFixture.StartAsync(options =>
            options.Channel("room.{id}", context => context.User.Identity?.Name == "alice"));

        var alice = await app.ConnectAsync("alice");
        await alice.SubscribeAsync("presence", "room.7");
        var bob = await app.ConnectAsync("bob");
        await Assert.ThrowsAsync<HubException>(() => bob.SubscribeAsync("presence", "room.7"));

        await app.Broadcaster.BroadcastAsync("message.sent", LiveChannel.Presence("room.7"));
        Assert.Equal("presence:room.7", (await alice.NextAsync()).ChannelKey);
    }

    [Fact]
    public async Task Async_rules_get_the_channel_values_and_services()
    {
        await using var app = await LiveHubFixture.StartAsync(options =>
            options.Channel("teams.{team}.boards.{board}", async context =>
            {
                await Task.Yield();
                return context.Services.GetService<IInertiaBroadcaster>() != null
                       && context.Values["team"] == "core"
                       && context.Values["board"] == "9"
                       && context.Channel.Type == LiveChannelType.Private
                       && !string.IsNullOrEmpty(context.ConnectionId);
            }));
        var client = await app.ConnectAsync("alice");

        await client.SubscribeAsync("private", "teams.core.boards.9");
        await Assert.ThrowsAsync<HubException>(() => client.SubscribeAsync("private", "teams.core.boards.8"));
    }

    [Theory]
    [InlineData("encrypted-private", "vault", "not supported")]
    [InlineData("secret", "vault", "Unknown live channel type")]
    [InlineData("public", " ", "empty")]
    public async Task Invalid_subscriptions_are_refused(string type, string name, string message)
    {
        await using var app = await LiveHubFixture.StartAsync(options => options.Channel("vault", _ => true));
        var client = await app.ConnectAsync("alice");

        var error = await Assert.ThrowsAsync<HubException>(() => client.SubscribeAsync(type, name));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Except_socket_id_skips_that_connection()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var sender = await app.ConnectAsync();
        var other = await app.ConnectAsync();
        foreach (var client in new[] { sender, other })
        {
            await client.SubscribeAsync("public", "dashboard");
            await client.SubscribeAsync("public", MarkerChannel);
        }

        await app.Broadcaster.BroadcastAsync("stats.changed", "dashboard", exceptSocketId: sender.ConnectionId);
        await app.Broadcaster.BroadcastAsync("marker", MarkerChannel);

        Assert.Equal("public:dashboard", (await other.NextAsync()).ChannelKey);
        Assert.Equal("public:marker", (await sender.NextAsync()).ChannelKey);
    }

    [Fact]
    public async Task Live_event_is_sent_on_each_of_its_channels()
    {
        await using var app = await LiveHubFixture.StartAsync(options => options.Channel("orders.{id}", _ => true));
        var client = await app.ConnectAsync("alice");
        var orderUpdated = new OrderUpdated(1);

        // Subscribe the way the client does: with the type and name the page's liveProps lists for the event.
        foreach (var channel in orderUpdated.Channels)
            await client.SubscribeAsync(channel.TypeName, channel.Name);

        await app.Broadcaster.BroadcastAsync(orderUpdated, new { order = new { id = 1 } });

        var first = await client.NextAsync();
        var second = await client.NextAsync();
        Assert.Equal(new[] { "private:orders.1", "public:orders" }, new[] { first.ChannelKey, second.ChannelKey }.Order());
        Assert.Equal("order.updated", first.Payload.GetProperty("__inertia").GetProperty("event").GetString());
    }

    [Fact]
    public async Task Duplicate_channels_get_the_event_once()
    {
        await using var app = await LiveHubFixture.StartAsync();
        var client = await app.ConnectAsync();
        await client.SubscribeAsync("public", "dashboard");
        await client.SubscribeAsync("public", MarkerChannel);

        await app.Broadcaster.BroadcastAsync("stats.changed", new LiveChannel[] { "dashboard", "dashboard" });
        await app.Broadcaster.BroadcastAsync("marker", MarkerChannel);

        Assert.Equal("public:dashboard", (await client.NextAsync()).ChannelKey);
        Assert.Equal("public:marker", (await client.NextAsync()).ChannelKey);
    }

    [Fact]
    public async Task Broadcasting_on_an_encrypted_channel_throws()
    {
        await using var app = await LiveHubFixture.StartAsync();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            app.Broadcaster.BroadcastAsync("x", LiveChannel.EncryptedPrivate("vault")));
    }

    [Theory]
    [InlineData("orders..id")]
    [InlineData("orders.{}")]
    [InlineData(" ")]
    public void Invalid_channel_patterns_are_rejected(string pattern)
    {
        Assert.ThrowsAny<ArgumentException>(() => new InertiaLiveOptions().Channel(pattern, _ => true));
    }

    static void OrdersRule(InertiaLiveOptions options)
        => options.Channel("orders.{id}", context => context.User.Identity?.Name == "alice" && context.Values["id"] == "1");

    private sealed class OrderUpdated : ILiveEvent
    {
        private readonly int _id;

        public OrderUpdated(int id) => _id = id;

        public string EventName => "order.updated";

        public IEnumerable<LiveChannel> Channels => new[] { LiveChannel.Private($"orders.{_id}"), "orders" };
    }
}
