using System.Text.Json.Serialization;

namespace Atom.Web.Browsers;

internal class DistributionPolicies
{
    public Extensions Policies { get; set; } = new();
}

internal class Policies
{
    public Extensions Extensions { get; set; } = new();
}

internal class Extensions
{
    [JsonPropertyName("Install")] public IEnumerable<string> Install { get; set; } = [];

    [JsonPropertyName("Uninstall")] public IEnumerable<string> Uninstall { get; set; } = [];

    [JsonPropertyName("Locked")] public IEnumerable<string> Locked { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(DistributionPolicies))]
internal partial class JsonDistributionPoliciesContext : JsonSerializerContext;