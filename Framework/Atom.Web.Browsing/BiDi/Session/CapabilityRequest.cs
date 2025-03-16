using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Atom.Buffers;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object containing capabilities requested when starting a new session.
/// </summary>
public class CapabilityRequest : IPooled
{
    /// <summary>
    /// Gets or sets a value indicating whether the browser should accept insecure (self-signed) SSL certificates.
    /// </summary>
    [JsonPropertyName("acceptInsecureCerts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAcceptInsecureCertificates { get; set; }

    /// <summary>
    /// Gets or sets the name of the browser.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrowserName { get; set; }

    /// <summary>
    /// Gets or sets the version of the browser.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrowserVersion { get; set; }

    /// <summary>
    /// Gets or sets the platform name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlatformName { get; set; }

    /// <summary>
    /// Gets or sets the proxy to use with this session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProxyConfiguration? Proxy { get; set; }

    /// <summary>
    /// Gets or sets the behavior of the session for handling user prompts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UserPromptHandler? UnhandledPromptBehavior { get; set; }

    /// <summary>
    /// Gets the dictionary containing additional capabilities to use with this session.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object?> AdditionalCapabilities { get; } = new Dictionary<string, object?>();

    /// <inheritdoc/>
    public void ClearForPool()
    {
        IsAcceptInsecureCertificates = default;
        BrowserName = default;
        BrowserVersion = default;
        PlatformName = default;
        Proxy = default;
        UnhandledPromptBehavior = default;
        AdditionalCapabilities.Clear();
    }

    /// <inheritdoc/>
    public static T Rent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>() where T : IPooled => ObjectPool<T>.Shared.Rent();

    /// <summary>
    /// Арендует экземпляр в пуле объектов.
    /// </summary>
    public static CapabilityRequest Rent() => Rent<CapabilityRequest>();

    /// <inheritdoc/>
    public static void Return<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(T value) where T : IPooled => ObjectPool<T>.Shared.Return(value, x => x.ClearForPool());
}