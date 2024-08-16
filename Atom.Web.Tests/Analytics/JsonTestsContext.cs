using System.Text.Json.Serialization;

namespace Atom.Web.Analytics.Tests;

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
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>), TypeInfoPropertyName = "Form")]
[JsonSerializable(typeof(Currency))]
[JsonSerializable(typeof(Country))]
public partial class JsonTestsContext : JsonSerializerContext;