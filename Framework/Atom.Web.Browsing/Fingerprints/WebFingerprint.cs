using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.Fingerprints;

/// <summary>
/// Представляет фингерпринт браузера.
/// </summary>
public class WebFingerprint : IWebFingerprint
{
    /// <inheritdoc/>
    public HardwareInfo Hardware { get; set; } = new();

    /// <inheritdoc/>
    [JsonIgnore]
    public virtual string UserAgent => BuildUserAgent();

    /// <summary>
    /// Собирает агента пользователя по информации из фингерпринта.
    /// </summary>
    /// <returns>Строка агента пользователя.</returns>
    protected virtual string BuildUserAgent() => string.Empty;
}