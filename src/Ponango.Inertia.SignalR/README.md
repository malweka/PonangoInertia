# Ponango.Inertia.SignalR

SignalR support for Inertia.js [live props](https://inertiajs.com/docs/v3/data-props/live-props) in
[Ponango.Inertia](https://github.com/malweka/PonangoInertia) apps. A live prop refreshes itself when a broadcast
event arrives. Ponango.Inertia lists, in the page object, the channels and events each live prop listens to. This
package carries those events over SignalR:

- `InertiaLiveHub`: clients subscribe to the channels their page lists, with per-channel authorization.
- `IInertiaBroadcaster`: sends an event, with or without new prop values, to everyone on its channels.

The browser side is the `@ponango/inertia-signalr` client transport (see [Client](#client)).

## Server

```csharp
builder.Services.AddInertia(options => { /* ... */ });
builder.Services.AddInertiaLive(options =>
{
    // Private and presence channels need a rule. Public channels are open to every connection.
    options.Channel("orders.{id}", context =>
        context.User.Identity?.IsAuthenticated == true && CanSeeOrder(context.User, context.Values["id"]));
});

var app = builder.Build();
app.UseInertia();
app.MapInertiaLive();               // the hub, at /inertia/live
```

Declare the live prop, preferably with an `ILiveEvent` that names the event and its channels once:

```csharp
public sealed class OrderUpdated(Order order) : ILiveEvent
{
    public string EventName => "order.updated";
    public IEnumerable<LiveChannel> Channels => new[] { LiveChannel.Private($"orders.{order.Id}") };
}

public IActionResult Show(int id)
{
    var order = _orders.Find(id);
    return Render("Orders/Show", new { order = Inertia.Live(() => _orders.Find(id), new OrderUpdated(order)) });
}
```

Broadcast when the data changes:

```csharp
public async Task<IActionResult> Ship(int id, [FromServices] IInertiaBroadcaster broadcaster)
{
    var order = _orders.Ship(id);

    // Clients reload the prop. SocketId skips the tab that made the change.
    await broadcaster.BroadcastAsync(new OrderUpdated(order), exceptSocketId: SocketId);

    return Back();
}
```

To spare the reload, send the new value in the event. Every subscriber receives the same payload, so only send
values that are the same for everyone:

```csharp
await broadcaster.BroadcastAsync(new OrderUpdated(order), new { order = OrderDto.From(order) });
```

`IInertiaBroadcaster` is a singleton, so background services can broadcast too.

### Authorization

- `options.Channel(pattern, rule)`: dot-separated segments, `{name}` captures one (`context.Values["name"]`). The
  first matching rule decides; a private or presence channel without a matching rule is refused.
- Rules get the user (`context.User`), the connection's `HttpContext`, its connection id and scoped
  `context.Services`, and may be async.
- The hub uses the app's authentication. Cookie authentication works out of the box because the hub is
  same-origin. To accept signed-in users only, use `app.MapInertiaLive().RequireAuthorization()`.
- Presence channels are authorized like private ones; member lists are not tracked. Encrypted private channels
  are a Pusher feature and are refused.

### Several servers

Use a SignalR backplane so every server sees every group: `builder.Services.AddInertiaLive(...).AddStackExchangeRedis(...)`
or Azure SignalR Service. `AddInertiaLive` returns the SignalR builder for this.

## Client

```js
import { HubConnectionBuilder } from '@microsoft/signalr'
import { signalR } from '@ponango/inertia-signalr'

const connection = new HubConnectionBuilder().withUrl('/inertia/live').withAutomaticReconnect().build()

createInertiaApp({
  live: signalR({ connection }),
  // ...
})
```

The `@ponango/inertia-signalr` npm package is not published yet. Until it is, a custom transport can implement
the hub contract below.

## Hub contract

| Direction | Call | Notes |
|---|---|---|
| client → server | `Subscribe(type, name)` | `type` is `public`, `private` or `presence`. Joins the SignalR group `{type}:{name}`; a refusal is a `HubException` |
| client → server | `Unsubscribe(type, name)` | Leaves the group; never fails |
| server → client | `inertia(channelKey, payload)` | `channelKey` is `{type}:{name}`; `payload` is `{ "__inertia": { "event": "...", "props": { ... } } }` |

SignalR drops a connection's groups when it reconnects, so a transport subscribes to its channels again after a
reconnect. The names are constants in `InertiaLiveProtocol`.
