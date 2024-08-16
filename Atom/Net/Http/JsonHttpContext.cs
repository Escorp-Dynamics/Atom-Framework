using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Net.Http;

/// <summary>
/// Представляет контекст сериализации для HTTP.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>), TypeInfoPropertyName = "Form")]
public partial class JsonHttpContext : JsonSerializerContext;