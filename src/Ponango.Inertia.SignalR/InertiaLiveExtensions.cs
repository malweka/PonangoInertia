using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ponango.Inertia.SignalR;

/// <summary>Registration of Inertia live props over SignalR.</summary>
public static class InertiaLiveExtensions
{
    /// <summary>
    /// Adds SignalR, the <see cref="InertiaLiveHub"/> channel authorization rules and <see cref="IInertiaBroadcaster"/>.
    /// Returns the SignalR builder, for example to add a Redis backplane.
    /// </summary>
    public static ISignalRServerBuilder AddInertiaLive(this IServiceCollection services, Action<InertiaLiveOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddSignalR();
        services.AddOptions<InertiaLiveOptions>();
        if (configure != null)
            services.Configure(configure);

        services.TryAddSingleton<IInertiaBroadcaster, InertiaBroadcaster>();
        return builder;
    }

    /// <summary>
    /// Maps <see cref="InertiaLiveHub"/> at <paramref name="pattern"/> (default <c>/inertia/live</c>), the URL the
    /// client transport connects to. Chain <c>RequireAuthorization()</c> to accept only signed-in users.
    /// </summary>
    public static HubEndpointConventionBuilder MapInertiaLive(this IEndpointRouteBuilder endpoints, string pattern = InertiaLiveProtocol.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapHub<InertiaLiveHub>(pattern);
    }
}
