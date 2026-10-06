using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ponango.Inertia;

/// <summary>
/// Recognizes the prop values the resolver may walk into for nested prop types and dot-notation paths:
/// dictionaries with string keys and C# anonymous objects. Other objects (POCOs, records, lists) are opaque and
/// serialize exactly as before.
/// </summary>
internal sealed class PropContainers
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> AnonymousMembers = new();

    private readonly JsonNamingPolicy? _propertyNamingPolicy;
    private readonly JsonNamingPolicy? _dictionaryKeyPolicy;
    private readonly bool _skipNullMembers;

    public PropContainers(JsonSerializerOptions? options)
    {
        _propertyNamingPolicy = options?.PropertyNamingPolicy;
        _dictionaryKeyPolicy = options?.DictionaryKeyPolicy;
        _skipNullMembers = options?.DefaultIgnoreCondition is JsonIgnoreCondition.WhenWritingNull
            or JsonIgnoreCondition.WhenWritingDefault;
    }

    public static bool IsContainer(object? value)
        => value switch
        {
            null or string => false,
            IDictionary dictionary => HasOnlyStringKeys(dictionary),
            _ => IsAnonymous(value.GetType())
        };

    /// <summary>
    /// The container's entries, with keys as they will appear in JSON (naming policies applied), so they line up
    /// with the dot paths the client sends.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> Entries(object container)
    {
        if (container is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = (string)entry.Key;
                yield return new(_dictionaryKeyPolicy?.ConvertName(key) ?? key, entry.Value);
            }

            yield break;
        }

        foreach (var member in AnonymousMembers.GetOrAdd(container.GetType(), ReadableMembers))
        {
            var value = member.GetValue(container);

            // Keep the same shape the serializer would produce for the anonymous object.
            if (value == null && _skipNullMembers)
                continue;

            yield return new(_propertyNamingPolicy?.ConvertName(member.Name) ?? member.Name, value);
        }
    }

    static bool HasOnlyStringKeys(IDictionary dictionary)
    {
        foreach (var key in dictionary.Keys)
        {
            if (key is not string)
                return false;
        }

        return true;
    }

    static bool IsAnonymous(Type type)
        => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
           && type.Name.Contains("AnonymousType", StringComparison.Ordinal);

    static PropertyInfo[] ReadableMembers(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();
}
