using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInertia(this IServiceCollection services)
    {
        services.AddScoped<InertiaContext>();
        return services;
    }
}