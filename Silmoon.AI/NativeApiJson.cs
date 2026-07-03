using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Silmoon.AI;

public static class NativeApiJson
{
    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = new NativeApiSerializationBinder(),
    };
}

public class NativeApiSerializationBinder : ISerializationBinder
{
    readonly DefaultSerializationBinder DefaultBinder = new();

    public Type BindToType(string assemblyName, string typeName)
    {
        var mappedTypeName = MapLegacyTypeName(typeName);
        try
        {
            return DefaultBinder.BindToType(assemblyName, mappedTypeName);
        }
        catch (JsonSerializationException) when (mappedTypeName != typeName)
        {
            var type = ResolveCurrentAssemblyType(mappedTypeName);
            if (type is not null) return type;
            throw;
        }
        catch (JsonSerializationException)
        {
            var type = ResolveCurrentAssemblyType(mappedTypeName);
            if (type is not null) return type;
            throw;
        }
    }

    public void BindToName(Type serializedType, out string assemblyName, out string typeName)
    {
        DefaultBinder.BindToName(serializedType, out assemblyName, out typeName);
    }

    static string MapLegacyTypeName(string typeName)
    {
        return typeName
            .Replace("Silmoon.AI.Models.OpenAI.Models.", "Silmoon.AI.OpenAI.Models.")
            .Replace("Silmoon.AI.Models.OpenAI.Enums.", "Silmoon.AI.OpenAI.Models.Enums.")
            .Replace("Silmoon.AI.Models.Anthropic.Models.", "Silmoon.AI.Anthropic.Models.");
    }

    static Type ResolveCurrentAssemblyType(string typeName)
    {
        return typeof(NativeApiSerializationBinder).Assembly.GetType(typeName);
    }
}
