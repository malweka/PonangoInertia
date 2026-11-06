using System.Text.Json;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// <c>Inertia.BroadcastPropsAsync</c>, the counterpart of inertia-laravel's <c>Inertia::broadcastProps()</c>: the
/// payload of a broadcast event that carries new values for live props.
/// </summary>
public class BroadcastPropsTests
{
    [Fact]
    public async Task Returns_the_inertia_envelope_and_nothing_else()
    {
        var payload = await Inertia.BroadcastPropsAsync(new { order = new { id = 1, total = 10 } });

        Assert.Equal("""{"__inertia":{"props":{"order":{"id":1,"total":10}}}}""", Serialize(payload));
    }

    [Fact]
    public async Task Event_and_socket_id_fill_the_envelope()
    {
        var payload = await Inertia.BroadcastPropsAsync(new { order = 1 }, @event: "order.updated", socketId: "abc");

        Assert.Equal("""{"__inertia":{"props":{"order":1},"event":"order.updated","socketId":"abc"}}""", Serialize(payload));
    }

    [Fact]
    public async Task Lazy_delegates_and_wrappers_are_resolved()
    {
        var payload = await Inertia.BroadcastPropsAsync(new
        {
            closure = (Func<int>)(() => 1),
            asyncClosure = (Func<Task<string>>)(() => Task.FromResult("two")),
            deferred = Inertia.Defer(() => 3),
            optional = Inertia.Optional(() => 4),
            always = Inertia.Always(() => 5),
            live = Inertia.Live(() => 6, "e", "c"),
            once = Inertia.Once(() => 7)
        });

        Assert.Equal(
            """{"closure":1,"asyncClosure":"two","deferred":3,"optional":4,"always":5,"live":6,"once":7}""",
            Serialize(Props(payload)));
    }

    [Fact]
    public async Task Rescued_props_are_left_out()
    {
        var payload = await Inertia.BroadcastPropsAsync(new
        {
            ok = 1,
            failed = Inertia.Defer(() => throw new InvalidOperationException("boom"), rescue: true),
            failedFromClosure = (Func<object>)(() => Inertia.Defer(() => throw new InvalidOperationException("boom")).Rescue())
        });

        Assert.Equal("""{"ok":1}""", Serialize(Props(payload)));
    }

    [Fact]
    public async Task Unrescued_failures_propagate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Inertia.BroadcastPropsAsync(new { failed = Inertia.Defer(() => throw new InvalidOperationException("boom")) }));
    }

    [Fact]
    public async Task Nested_containers_with_prop_types_are_walked()
    {
        var payload = await Inertia.BroadcastPropsAsync(new
        {
            auth = new { user = new { name = "Ada" }, count = (Func<int>)(() => 3) }
        });

        Assert.Equal("""{"auth":{"user":{"name":"Ada"},"count":3}}""", Serialize(Props(payload)));
    }

    [Fact]
    public async Task Dot_path_keys_stay_flat_and_literal()
    {
        var payload = await Inertia.BroadcastPropsAsync(new Dictionary<string, object?>
        {
            ["auth.notifications"] = (Func<int>)(() => 3),
            ["order"] = 1
        });

        Assert.Equal("""{"auth.notifications":3,"order":1}""", Serialize(Props(payload)));
    }

    [Fact]
    public async Task A_value_that_is_not_a_container_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Inertia.BroadcastPropsAsync(new[] { 1, 2 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Inertia.BroadcastPropsAsync("order"));
    }

    static IDictionary<string, object?> Props(IDictionary<string, object?> payload)
        => (IDictionary<string, object?>)((IDictionary<string, object?>)payload["__inertia"]!)["props"]!;

    static string Serialize(object value) => JsonSerializer.Serialize(value);
}
