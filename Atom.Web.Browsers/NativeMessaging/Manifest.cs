using System.Text.Json.Serialization;

namespace Atom.Web.Browsers.NativeMessaging;

/// <summary>
/// Представляет манифест.
/// </summary>
public class Manifest
{
    /// <summary>
    /// Имя приложения.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Описание приложения.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Путь к исполняемому файлу приложения.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Тип обмена данными.
    /// </summary>
    public string Type { get; set; } = "stdio";

    /// <summary>
    /// Коллекция расширений, которым предоставлен доступ к приложению.
    /// </summary>
    /// <value></value>
    [JsonPropertyName("allowed_extensions")]
    public IEnumerable<string> AllowedExtensions { get; set; } = [];
}

/// <summary>
/// Представляет контекст сериализации манифеста.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(Manifest))]
public partial class JsonManifestContext : JsonSerializerContext;