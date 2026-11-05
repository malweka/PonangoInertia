# @ponango/inertia-signalr

A SignalR transport for Inertia.js [live props](https://inertiajs.com/docs/v3/data-props/live-props), for
ASP.NET Core apps that use [Ponango.Inertia](https://github.com/malweka/PonangoInertia) and its
`Ponango.Inertia.SignalR` package.

A live prop refreshes itself when a broadcast event arrives. The server lists, in each page, the channels and events
its live props listen to. This transport joins those channels on the server's SignalR hub and hands the events to
Inertia, which reloads the props or takes their new values from the event.

## Install

```sh
npm install @ponango/inertia-signalr @microsoft/signalr
```

It works with `@inertiajs/vue3`, `@inertiajs/react` and `@inertiajs/svelte` 3.11 or later.

## Setup

```js
import { createInertiaApp } from '@inertiajs/vue3'
import { HubConnectionBuilder } from '@microsoft/signalr'
import { signalR } from '@ponango/inertia-signalr'

const connection = new HubConnectionBuilder()
  .withUrl('/inertia/live')        // the hub mapped by MapInertiaLive()
  .withAutomaticReconnect()
  .build()

createInertiaApp({
  live: signalR({ connection }),
  // resolve, setup, ...
})
```

The server side, from `Ponango.Inertia.SignalR`:

```csharp
builder.Services.AddInertiaLive(options =>
    options.Channel("orders.{id}", context => CanSeeOrder(context.User, context.Values["id"])));

app.MapInertiaLive();
```

See the [Ponango.Inertia.SignalR README](https://github.com/malweka/PonangoInertia/blob/main/src/Ponango.Inertia.SignalR/README.md)
for declaring live props and broadcasting events.

## Options

| Option | Default | |
|---|---|---|
| `connection` | (required) | A `HubConnection`, or a function returning one, called once the first time it is needed |
| `start` | `true` | Start the connection when a channel is needed and it is disconnected. With `false`, the transport waits for your own `connection.start()` |
| `method` | `'inertia'` | The client method the server sends events to |
| `throttle` | Inertia's default | Passed to Inertia: the minimum time between reloads of a prop, in milliseconds |
| `pauseWhenHidden` | `true` | Passed to Inertia: hold reloads while the tab is hidden |

## How it works

- Each channel is joined once, with the hub's `Subscribe(type, name)`, however many props listen to it, and left
  with `Unsubscribe(type, name)` when no prop listens anymore.
- The server sends every event to `inertia(channelKey, payload)`. The transport passes the payload to the
  channel's listeners, and Inertia reads the event name from `payload.__inertia.event`.
- A channel the hub refuses (a private channel whose rule says no) logs
  `Inertia could not subscribe to the live channel "…"`. Its props stay as they are.
- The socket id is the connection id. Inertia sends it as `X-Socket-Id`, so the server can skip the tab that made a
  change (`exceptSocketId: SocketId`).
- SignalR drops a connection's groups when it reconnects. After a reconnect the transport joins every channel
  again, then tells Inertia the connection is back, and Inertia refreshes every live prop, since events may have
  been missed.

`withAutomaticReconnect()` gives up after four attempts by default. To keep trying, pass a retry policy:

```js
.withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 5000 })
```

A connection you stop and start again yourself is not rejoined: rely on automatic reconnects instead.
