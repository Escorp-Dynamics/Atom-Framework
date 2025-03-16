using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Object containing data about a partition key for a browser cookie.
/// </summary>
public class PartitionKey
{
    /// <summary>
    /// Gets the ID of the user context of the cookie partition key.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonInclude]
    public string? UserContextId { get; internal set; }

    /// <summary>
    /// Gets the source origin of the cookie partition key.
    /// </summary>
    [JsonPropertyName("sourceOrigin")]
    [JsonInclude]
    public string? SourceOrigin { get; internal set; }

    /// <summary>
    /// Gets read-only dictionary of additional properties deserialized with this message.
    /// </summary>
    [JsonIgnore]
    public ReceivedDataDictionary AdditionalData
    {
        get
        {
            if (SerializableAdditionalData.Count > 0 && field.Count is 0) field = JsonConverterUtilities.ConvertIncomingExtensionData(SerializableAdditionalData);
            return field;
        }
    } = ReceivedDataDictionary.Empty;

    /// <summary>
    /// Gets additional properties deserialized with this message.
    /// </summary>
    [JsonExtensionData]
    [JsonInclude]
    internal Dictionary<string, JsonElement> SerializableAdditionalData { get; set; } = [];
}