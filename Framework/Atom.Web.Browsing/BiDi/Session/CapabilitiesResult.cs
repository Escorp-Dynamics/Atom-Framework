using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object containing the capabilities returned by a new session.
/// </summary>
public class CapabilitiesResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilitiesResult"/> class.
    /// </summary>
    [JsonConstructor]
    internal CapabilitiesResult() { }

    /// <summary>
    /// Gets a value indicating whether the browser should accept insecure (self-signed) SSL certificates.
    /// </summary>
    [JsonPropertyName("acceptInsecureCerts")]
    [JsonRequired]
    [JsonInclude]
    public bool IsAcceptInsecureCertificates { get; internal set; }

    /// <summary>
    /// Gets the name of the browser.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string BrowserName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the version of the browser.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string BrowserVersion { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the platform name.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string PlatformName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this session supports setting the size of the browser window.
    /// </summary>
    [JsonPropertyName("setWindowRect")]
    // TODO (Issue #18): Uncomment the JsonRequired attribute once https://bugzilla.mozilla.org/show_bug.cgi?id=1916522 is fixed.
    // [JsonRequired]
    [JsonInclude]
    public bool IsSetWindowRect { get; internal set; }

    /// <summary>
    /// Gets a value indicating the WebSocket URL used by this connection.
    /// </summary>
    [JsonInclude]
    public Uri? WebSocketUrl { get; internal set; }

    /// <summary>
    /// Gets a value containing the default user agent string for this browser.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string UserAgent { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets a read-only dictionary of additional capabilities specified by this session.
    /// </summary>
    [JsonIgnore]
    public ReceivedDataDictionary AdditionalCapabilities
    {
        get
        {
            if (SerializableAdditionalCapabilities.Count > 0 && field.Count == 0)
            {
                field = JsonConverterUtilities.ConvertIncomingExtensionData(SerializableAdditionalCapabilities);
            }

            return field;
        }
    } = ReceivedDataDictionary.Empty;

    /// <summary>
    /// Gets the behavior of the session for unhandled user prompts.
    /// </summary>
    [JsonIgnore]
    public UserPromptHandlerResult? UnhandledPromptBehavior
    {
        get
        {
            if (SerializableUnhandledPromptBehavior is not null && field is null) field = new UserPromptHandlerResult(SerializableUnhandledPromptBehavior);

            return field;
        }
    }

    /// <summary>
    /// Gets the proxy used by this session.
    /// </summary>
    [JsonIgnore]
    public ProxyConfigurationResult? Proxy
    {
        get
        {
            if (SerializableProxy is not null && field is null)
            {
                switch (SerializableProxy.ProxyType)
                {
                    case ProxyType.Direct:
                        field = new DirectProxyConfigurationResult((DirectProxyConfiguration)SerializableProxy);
                        break;
                    case ProxyType.System:
                        field = new SystemProxyConfigurationResult((SystemProxyConfiguration)SerializableProxy);
                        break;
                    case ProxyType.AutoDetect:
                        field = new AutoDetectProxyConfigurationResult((AutoDetectProxyConfiguration)SerializableProxy);
                        break;
                    case ProxyType.ProxyAutoConfig:
                        field = new PacProxyConfigurationResult((PacProxyConfiguration)SerializableProxy);
                        break;
                    case ProxyType.Manual:
                        field = new ManualProxyConfigurationResult((ManualProxyConfiguration)SerializableProxy);
                        break;
                }
            }

            return field;
        }
    }

    /// <summary>
    /// Gets or sets the proxy used for this session.
    /// </summary>
    [JsonPropertyName("proxy")]
    [JsonInclude]
    internal ProxyConfiguration? SerializableProxy { get; set; }

    /// <summary>
    /// Gets or sets the behavior of the session for unhandled user prompts.
    /// </summary>
    [JsonPropertyName("unhandledPromptBehavior")]
    [JsonInclude]
    internal UserPromptHandler? SerializableUnhandledPromptBehavior { get; set; }

    /// <summary>
    /// Gets or sets the dictionary containing additional, un-enumerated capabilities for deserialization purposes.
    /// </summary>
    [JsonExtensionData]
    [JsonInclude]
    internal IDictionary<string, JsonElement> SerializableAdditionalCapabilities { get; set; } = new Dictionary<string, JsonElement>();
}