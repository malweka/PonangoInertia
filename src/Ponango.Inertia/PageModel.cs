using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ponango.Inertia
{
    public class PageModel
    {
        // Core properties (always present)
        public string Component { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public object Props { get; set; } = new { };

        // History management (v2/v3 - only serialized when true)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EncryptHistory { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ClearHistory { get; set; }

        // Navigation (v2/v3)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PreserveFragment { get; set; }

        // Merge props (v2/v3 - controls how client merges data during navigation)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? MergeProps { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? PrependProps { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? DeepMergeProps { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? MatchPropsOn { get; set; }

        // Infinite scroll (v2/v3)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? ScrollProps { get; set; }

        // Deferred props (v2/v3 - group name -> list of prop keys)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, List<string>>? DeferredProps { get; set; }

        // Rescued deferred props (v3 - keys whose callbacks failed and were omitted)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? RescuedProps { get; set; }

        // Shared props (v2/v3 - top-level prop keys registered via Share())
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? SharedProps { get; set; }

        // Once props (v2/v3 - props resolved once and cached client-side)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? OnceProps { get; set; }

        // Flash data for this response (v3 - only serialized when present; the client exposes it as
        // page.flash and does not persist it in history state)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Flash { get; set; }

        // Big integers (v3 - when true, the client revives {"$bigint": "..."} markers as BigInt values)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PreserveBigIntegers { get; set; }

        public string ToJson(IJsonSerializerOptionBuilder serializerOptions)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };

            serializerOptions.SetSerializerOptions(options);

            // Inserted first so they take precedence over any user converter for the same types.
            if (PreserveBigIntegers == true)
            {
                options.Converters.Insert(0, new BigIntegerJsonElementConverter());
                options.Converters.Insert(0, new BigIntegerConverterFactory());
            }

            return JsonSerializer.Serialize(this, options);
        }
    }
}
