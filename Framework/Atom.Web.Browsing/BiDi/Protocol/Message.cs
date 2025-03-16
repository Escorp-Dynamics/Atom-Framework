using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing data about a WebDriver Bidi message.
/// </summary>
public class Message
{
    /// <summary>
    /// Gets the type of message.
    /// </summary>
    [JsonRequired]
    [JsonPropertyName("type")]
    [JsonInclude]
    public string Type { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets read-only dictionary of additional properties deserialized with this message.
    /// </summary>
    [JsonIgnore]
    public ReceivedDataDictionary AdditionalData
    {
        get
        {
            if (SerializableAdditionalData.Count > 0 && field.Count is 0)
                field = JsonConverterUtilities.ConvertIncomingExtensionData(SerializableAdditionalData);

            return field;
        }
    } = ReceivedDataDictionary.Empty;

    /// <summary>
    /// Gets additional properties deserialized with this message.
    /// </summary>
    [JsonExtensionData]
    [JsonInclude]
    internal IDictionary<string, JsonElement> SerializableAdditionalData { get; set; } = new Dictionary<string, JsonElement>();
}