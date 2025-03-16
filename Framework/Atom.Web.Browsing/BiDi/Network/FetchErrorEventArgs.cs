using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Object containing event data for events raised by before a network request is sent.
/// </summary>
public class FetchErrorEventArgs : BaseNetworkEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FetchErrorEventArgs"/> class.
    /// </summary>
    public FetchErrorEventArgs() : base() { }

    /// <summary>
    /// Gets the error text of the fetch error.
    /// </summary>
    [JsonPropertyName("errorText")]
    [JsonRequired]
    [JsonInclude]
    public string ErrorText { get; internal set; } = string.Empty;
}