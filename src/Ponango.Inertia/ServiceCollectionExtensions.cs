using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInertia(this IServiceCollection services, Action<JsonSerializerOptions>? serializerOptionsMethod = null)
    {
        services.AddScoped<InertiaContext>();

        if (serializerOptionsMethod == null)
            serializerOptionsMethod = DefaultSerializerOptions;

        services.AddSingleton<IJsonSerializerOptionBuilder>(
            new DefaultJsonSerializerOptionBuilder(serializerOptionsMethod));

        return services;
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
    private Action<JsonSerializerOptions> serializerOptionsMethod;

    public DefaultJsonSerializerOptionBuilder(Action<JsonSerializerOptions> serializerOptionsMethod)
    {
        this.serializerOptionsMethod = serializerOptionsMethod;
    }

    public void SetSerializerOptions(JsonSerializerOptions options)
    {
        serializerOptionsMethod(options);
    }
}