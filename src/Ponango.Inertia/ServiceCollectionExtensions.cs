using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Inertia services with an optional serializer configuration callback.
    /// </summary>
    public static IServiceCollection AddInertia(this IServiceCollection services, Action<JsonSerializerOptions>? serializerOptionsMethod = null)
    {
        return AddInertia(services, options =>
        {
            if (serializerOptionsMethod != null)
                options.JsonSerializerOptions = serializerOptionsMethod;
        });
    }

    /// <summary>
    /// Registers Inertia services with the full <see cref="InertiaOptions"/> configuration surface.
    /// </summary>
    public static IServiceCollection AddInertia(this IServiceCollection services, Action<InertiaOptions>? configure)
    {
        var options = new InertiaOptions();
        configure?.Invoke(options);

        services.AddSingleton(Options.Create(options));
        services.AddHttpContextAccessor();
        services.AddScoped<InertiaFlash>();
        services.AddScoped<InertiaContext>();

        var jsonAction = options.JsonSerializerOptions ?? DefaultSerializerOptions;
        services.AddSingleton<IJsonSerializerOptionBuilder>(
            new DefaultJsonSerializerOptionBuilder(jsonAction));

        return services;
    }

    /// <summary>
    /// Registers the Inertia middleware. Call this after UseRouting() and before UseEndpoints()
    /// (or MapControllers()) in your middleware pipeline.
    /// </summary>
    public static IApplicationBuilder UseInertia(this IApplicationBuilder app)
    {
        return app.UseMiddleware<InertiaMiddleware>();
    }

    static void DefaultSerializerOptions(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.WriteIndented = false;
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    }
}

public interface IJsonSerializerOptionBuilder
{
    void SetSerializerOptions(JsonSerializerOptions options);
}

class DefaultJsonSerializerOptionBuilder : IJsonSerializerOptionBuilder
{
    private readonly Action<JsonSerializerOptions> _configure;

    public DefaultJsonSerializerOptionBuilder(Action<JsonSerializerOptions> configure)
    {
        _configure = configure;
    }

    public void SetSerializerOptions(JsonSerializerOptions options) => _configure(options);
}
