using System.Text.Json.Serialization;

namespace Atom.Web.Browsers.Firefox;

internal sealed class DistributionPolicies
{
    public Policies Policies { get; set; } = new();
}

internal sealed class Policies
{
    [JsonPropertyName("Extensions")]
    public ExtensionsPolicies Extensions { get; set; } = new();
}

internal sealed class ExtensionsPolicies
{
    [JsonPropertyName("Install")]
    public IEnumerable<string> Installed { get; set; } = [];

    [JsonPropertyName(name: "Locked")]
    public IEnumerable<string> Locked { get; set; } = [];

    [JsonPropertyName(name: "AllowedInPrivateBrowsing")]
    public IEnumerable<string> AllowedInPrivateBrowsing { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(DistributionPolicies))]
internal sealed partial class JsonDistributionPoliciesContext : JsonSerializerContext;