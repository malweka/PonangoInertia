using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia.SignalR.Tests;

/// <summary>
/// An in-memory app with the Inertia live hub mapped, and real SignalR client connections to it.
/// Requests carry the user name in <c>X-Test-User</c>.
/// </summary>
internal sealed class LiveHubFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<HubConnection> _connections = new();

    private LiveHubFixture(WebApplication app) => _app = app;

    public IInertiaBroadcaster Broadcaster => _app.Services.GetRequiredService<IInertiaBroadcaster>();

    public static async Task<LiveHubFixture> StartAsync(Action<InertiaLiveOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(TestAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddInertiaLive(configure);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInertiaLive();
        await app.StartAsync();

        return new LiveHubFixture(app);
    }

    public async Task<LiveClient> ConnectAsync(string? user = null)
    {
        var server = _app.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl($"http://localhost{InertiaLiveProtocol.DefaultPath}", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (user != null)
                    options.Headers["X-Test-User"] = user;
            })
            .Build();

        var client = new LiveClient(connection);
        await connection.StartAsync();
        _connections.Add(connection);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();

        await _app.DisposeAsync();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Scheme = "Test";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrEmpty(user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user) }, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}

/// <summary>A connected client that records the live events it receives.</summary>
internal sealed class LiveClient
{
    private readonly Channel<(string ChannelKey, JsonElement Payload)> _received = Channel.CreateUnbounded<(string, JsonElement)>();

    public LiveClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<string, JsonElement>(InertiaLiveProtocol.ClientMethod,
            (channelKey, payload) => _received.Writer.TryWrite((channelKey, payload)));
    }

    public HubConnection Connection { get; }

    public string ConnectionId => Connection.ConnectionId!;

    public Task SubscribeAsync(string type, string name)
        => Connection.InvokeAsync(InertiaLiveProtocol.SubscribeMethod, type, name);

    public Task UnsubscribeAsync(string type, string name)
        => Connection.InvokeAsync(InertiaLiveProtocol.UnsubscribeMethod, type, name);

    /// <summary>The next event received, failing after a few seconds.</summary>
    public async Task<(string ChannelKey, JsonElement Payload)> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _received.Reader.ReadAsync(timeout.Token);
    }
}
