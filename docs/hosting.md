# Hosting behind a proxy and in containers

Ponango.Inertia runs anywhere ASP.NET Core does. Behind a reverse proxy, and with more than one instance, it needs
the same setup as any ASP.NET Core app, plus a little more for live props over SignalR. This page covers that setup
in general, then for [Azure Container Apps](#azure-container-apps) and [OpenShift](#openshift).

| You run | You need |
|---|---|
| Any app behind a TLS-terminating proxy | [Forwarded headers](#forwarded-headers) |
| More than one instance | [Shared Data Protection keys](#shared-data-protection-keys) |
| Live props, one instance | Nothing more: the platforms below pass WebSockets through |
| Live props, more than one instance | [Sticky sessions and a backplane](#live-props-on-several-instances), or Azure SignalR Service |

## Forwarded headers

A proxy or platform ingress usually ends TLS and forwards the request to the app over plain `http`. Without
forwarded headers, the app believes it is served from `http://host:80`. Ponango.Inertia compares URLs with the
request's scheme, host and port to tell its own pages from other sites, so the wrong scheme breaks:

- `Back()` and [empty responses redirected back](advanced-topics.md#empty-responses-redirect-back): the
  `Referer` (`https://host/...`, port 443) looks like another site, so users land on the app root instead of the
  page they came from;
- [redirects](advanced-topics.md#external-redirects) to an absolute URL of the app itself: they look external, so
  they become a `409` and the browser reloads the whole page.

Apply the proxy's `X-Forwarded-Proto` header before anything else in the pipeline:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The ingress address is not known in advance. Only do this when the app cannot be reached except through
    // the proxy, as in the platforms below.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();   // first
// ...
app.UseInertia();
```

In a container, setting the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` does the same without
code. Add `ForwardedHeaders.XForwardedHost` only if your proxy rewrites the `Host` header; the platforms below keep
it.

To check it, log `Request.Scheme` behind the proxy: it must be `https`.

## Shared Data Protection keys

ASP.NET Core encrypts authentication cookies and, with the default cookie TempData provider, TempData with
[Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview). Each
instance creates its own keys unless you share them, so a cookie written by one instance cannot be read by
another. For Ponango.Inertia that means:

- [flash data](advanced-topics.md#flash-messages) and
  [validation errors kept across a redirect](advanced-topics.md#redirecting-back-with-errors) are stored in
  TempData. When the redirect lands on another instance, they are silently lost;
- users are signed out when they reach another instance, and private live channels refuse them.

Sticky sessions hide this until a deploy or a scale-in replaces the instance. Store the keys where every instance
reads them:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("my-app")
    .PersistKeysToAzureBlobStorage(blobUri, credential);       // Azure.Extensions.AspNetCore.DataProtection.Blobs
    // or .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys")  // Microsoft.AspNetCore.DataProtection.StackExchangeRedis
    // or .PersistKeysToFileSystem(new DirectoryInfo("/keys"))  // a volume every instance mounts
```

Protect the keys at rest too (`ProtectKeysWithAzureKeyVault`, or a certificate) in production.

## Live props on several instances

[Live props](advanced-topics.md#live-props) over [`Ponango.Inertia.SignalR`](../src/Ponango.Inertia.SignalR/README.md)
work through a reverse proxy without changes on one instance. With several, two things are needed:

- **Sticky sessions.** A SignalR connection starts with a `negotiate` request, then opens the connection. Both
  must reach the same instance, or the connection fails. (A client that skips negotiation and uses WebSockets
  only does not need them, but loses the fallbacks.)
- **A backplane.** `IInertiaBroadcaster` sends to the clients connected to its own instance. A backplane carries
  every broadcast to every instance:

  ```csharp
  builder.Services.AddInertiaLive(options => { /* channel rules */ })
      .AddStackExchangeRedis(redisConnectionString);   // Microsoft.AspNetCore.SignalR.StackExchangeRedis
  ```

**Azure SignalR Service** replaces both. Browsers connect to the service instead of to your instances, so no
sticky sessions or Redis are needed, and instances scale freely:

```csharp
builder.Services.AddInertiaLive(options => { /* channel rules */ })
    .AddAzureSignalR(connectionString);                // Microsoft.Azure.SignalR
```

`MapInertiaLive()`, the channel rules and the client transport stay the same.

Deploys and restarts drop every connection. A client built with `withAutomaticReconnect(keepRetrying)` (see the
[`@ponango/inertia-signalr` README](../clients/inertia-signalr/README.md#reconnecting)) reconnects, joins its
channels again and refreshes its live props, so nothing is missed.

## Azure Container Apps

- **TLS and forwarded headers:** the ingress ends TLS and sends `X-Forwarded-Proto`. Set up
  [forwarded headers](#forwarded-headers).
- **WebSockets:** HTTP ingress supports them, with nothing to configure.
- **Several replicas:** use Azure SignalR Service, the simplest choice here. Otherwise turn on session affinity
  (Ingress > Session affinity, or `ingress.stickySessions.affinity: sticky`), which needs single revision mode,
  and add a Redis backplane. Either way, share the [Data Protection keys](#shared-data-protection-keys).
- **Minimum replicas:** keep at least one. Scaling to zero closes every live connection, and the next visitor
  waits for a cold start.
- **HTTP scale rule:** each open SignalR connection counts as a concurrent request, so set the threshold with the
  number of connected users in mind, or scale on CPU instead.

## OpenShift

- **TLS and forwarded headers:** edge and re-encrypt routes end TLS at the router and send `X-Forwarded-Proto`.
  Set up [forwarded headers](#forwarded-headers).
- **WebSockets:** routes support them, with nothing to configure. Once a connection is upgraded, the router's
  tunnel timeout applies (one hour by default), and SignalR's keep-alive pings, every 15 seconds, stay well inside
  it.
- **Route timeout:** the default of 30 seconds is too short for SignalR's long polling fallback, whose requests
  last up to 90 seconds. Raise it on the route:

  ```yaml
  metadata:
    annotations:
      haproxy.router.openshift.io/timeout: 2m
  ```

- **Several pods:** edge and re-encrypt routes keep each browser on one pod with a cookie by default; do not set
  `haproxy.router.openshift.io/disable_cookies`. A passthrough route cannot read cookies, so balance it by source
  address (`haproxy.router.openshift.io/balance: source`). Add a Redis backplane and share the
  [Data Protection keys](#shared-data-protection-keys).
- **Port:** OpenShift runs containers as an arbitrary non-root user, which cannot bind ports below 1024. The .NET 8
  and later images listen on 8080 by default, which works.
