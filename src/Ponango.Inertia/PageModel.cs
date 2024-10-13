using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ponango.Inertia
{
    public class PageModel
    {
        public string Component { get; set; }
        public string Url { get; set; }
        public string Version { get; set; }
        public object Props { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, SerializerOptions);
        }

        private static JsonSerializerOptions SerializerOptions { get; }

        static PageModel()
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
            SerializerOptions = jsonOptions;
        }
    }
}