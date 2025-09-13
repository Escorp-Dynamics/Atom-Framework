using System.Text.Json.Serialization;

namespace Atom.Net.Http.Tests;

internal sealed class HttpsRequestTestData
{
    public required string Method { get; set; }
    public required string Protocol { get; set; }
    public required string Host { get; set; }
    public required string Path { get; set; }
    public required string Ip { get; set; }
    public required IReadOnlyDictionary<string, string> Headers { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.Strict,
    PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
)]
[JsonSerializable(typeof(HttpsRequestTestData))]
internal sealed partial class JsonContext : JsonSerializerContext;