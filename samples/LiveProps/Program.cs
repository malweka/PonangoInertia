using LiveProps;
using Ponango.Inertia;
using Ponango.Inertia.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddInertia((InertiaOptions options) => options.RootView = "Inertia");
builder.Services.AddSingleton<IAssetVersionProvider, AssetVersionProvider>();
builder.Services.AddSingleton<OrderStore>();

// Private channels need a rule. The sample "signs in" with a user cookie; a real app checks its own user.
builder.Services.AddInertiaLive(options =>
    options.Channel("orders.{id}", context =>
        context.HttpContext?.Request.Cookies["user"] == "alice" && context.Values["id"] == "1"));

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseInertia();
app.MapDefaultControllerRoute();
app.MapInertiaLive();

// Changes made "elsewhere" (another user, a background job), used by the e2e check and the page's buttons.
app.MapPost("/simulate/order", async (OrderStore store, IInertiaBroadcaster broadcaster) =>
{
    var order = store.Advance();
    await broadcaster.BroadcastAsync(new OrderUpdated(order.Id));   // clients reload the prop
    return Results.Ok(order);
});

app.MapPost("/simulate/ticker", async (OrderStore store, IInertiaBroadcaster broadcaster) =>
{
    var ticker = store.Tick();
    await broadcaster.BroadcastAsync("ticker.ticked", "ticker", new { ticker });   // clients take the value
    return Results.Ok(ticker);
});

app.Run();
