using Microsoft.AspNetCore.Mvc;
using Ponango.Inertia;
using Ponango.Inertia.SignalR;
// Inside an InertiaController, a bare "Inertia" means the controller's obsolete Inertia(...) method.
using Props = Ponango.Inertia.Inertia;

namespace LiveProps;

public sealed class AssetVersionProvider : IAssetVersionProvider
{
    public string GetAssetVersion() => "1";
}

public sealed record Order(int Id, string Status, int Revision);

/// <summary>An in-memory order and ticker, enough to show live props.</summary>
public sealed class OrderStore
{
    private static readonly string[] Statuses = { "placed", "packed", "shipped", "delivered" };
    private readonly object _lock = new();
    private int _revision;
    private int _ticker;

    public Order Get(int id)
    {
        lock (_lock) return new Order(id, Statuses[_revision % Statuses.Length], _revision);
    }

    public Order Advance()
    {
        lock (_lock)
        {
            _revision++;
            return Get(1);
        }
    }

    public int Ticker
    {
        get { lock (_lock) return _ticker; }
    }

    public int Tick()
    {
        lock (_lock) return ++_ticker;
    }
}

/// <summary>Names the event and its channel once, for both the live prop and the broadcast.</summary>
public sealed class OrderUpdated(int id) : ILiveEvent
{
    public string EventName => "order.updated";

    public IEnumerable<LiveChannel> Channels => new[] { LiveChannel.Private($"orders.{id}") };
}

public sealed class OrdersController(OrderStore store, IInertiaBroadcaster broadcaster) : InertiaController
{
    [HttpGet("/")]
    public IActionResult Index() => Redirect("/orders/1");

    [HttpGet("/orders/{id:int}")]
    public IActionResult Show(int id)
        => Render("Orders/Show", new
        {
            order = Props.Live(() => store.Get(id), new OrderUpdated(id)),
            ticker = Props.Live(() => store.Ticker, "ticker.ticked", "ticker"),
            user = Request.Cookies["user"]
        });

    // A change made from this tab: the broadcast skips this connection (SocketId), and the redirect back refreshes it.
    [HttpPost("/orders/{id:int}/advance")]
    public async Task<IActionResult> Advance(int id)
    {
        store.Advance();
        await broadcaster.BroadcastAsync(new OrderUpdated(id), exceptSocketId: SocketId);
        return Back();
    }
}
