using System.Text.Json;
using Atom.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read-only proxy settings object that is being used by the browser in this session.
/// </summary>
public class ProxyConfigurationResult
{
    private readonly ProxyConfiguration proxy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyConfigurationResult"/> class.
    /// </summary>
    /// <param name="proxy">The <see cref="ProxyConfiguration"/> returned by the new session command.</param>
    internal ProxyConfigurationResult(ProxyConfiguration proxy) => this.proxy = proxy;

    /// <summary>
    /// Gets the type of proxy configuration.
    /// </summary>
    public ProxyType ProxyType => proxy.ProxyType;

    /// <summary>
    /// Gets a read-only dictionary of additional properties deserialized with this proxy result.
    /// </summary>
    public ReceivedDataDictionary AdditionalData
    {
        get
        {
            if (proxy.AdditionalData.Count > 0 && field.Count is 0) field = JsonConverterUtilities.ConvertIncomingExtensionData(ConvertIncomingExtensionData());

            return field;
        }
    } = ReceivedDataDictionary.Empty;
    
    private Dictionary<string, JsonElement> ConvertIncomingExtensionData()
    {
        var convertedData = new Dictionary<string, JsonElement>();

        foreach (var pair in proxy.AdditionalData)
            convertedData[pair.Key] = pair.Value.AsJsonElement();

        return convertedData;
    }

    /// <summary>
    /// Gets the ProxyConfigurationResult as the type-specific type.
    /// </summary>
    /// <typeparam name="T">A <see cref="ProxyConfigurationResult"/> type to convert to.</typeparam>
    /// <returns>The <see cref="ProxyConfigurationResult"/> type to convert to.</returns>
    public T ProxyConfigurationResultAs<T>() where T : ProxyConfigurationResult => (T)this;

    /// <summary>
    /// Gets the underlying proxy configuration as the type-specific proxy configuration type.
    /// </summary>
    /// <typeparam name="T">A <see cref="ProxyConfiguration"/> type.</typeparam>
    /// <returns>The proxy configuration ad the type-specific proxy configuration type.</returns>
    protected T ProxyConfigurationAs<T>() where T : ProxyConfiguration => (T)proxy;
}