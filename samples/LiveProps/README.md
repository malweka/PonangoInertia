# Live props sample

An ASP.NET Core app with Inertia live props over SignalR: `Ponango.Inertia`, `Ponango.Inertia.SignalR`, and the
`@ponango/inertia-signalr` client transport (from `clients/inertia-signalr`), with Vue.

The order page has two live props:

- `order` listens on the private channel `orders.1` (only the user `alice` may join it). An event makes the page
  reload it.
- `ticker` listens on the public channel `ticker`. The event carries the new value, so the page takes it without a
  request.

## Run it

```sh
# once: build the client transport the sample links to
cd clients/inertia-signalr && npm ci && npm run build && cd -

cd samples/LiveProps/ClientApp && npm ci && npm run build && cd ..
dotnet run
```

Open the printed URL, set a `user=alice` cookie (for example `document.cookie = "user=alice"` in the console) and
reload, then open the page in a second tab. The buttons change the order or the ticker from the current tab or
"from elsewhere", and both tabs update.

## End-to-end check

```sh
cd samples/LiveProps && dotnet build && cd ClientApp && npm run e2e
```

`e2e/run.mjs` starts the sample, drives a headless Microsoft Edge (set `BROWSER_CHANNEL=chrome` for Chrome) and
checks reloads, values in events, private channel refusal, sender exclusion with `X-Socket-Id`, and rejoining after
the server restarts and after the app stops and starts the connection. CI runs it on every pull request.
