namespace Atom.Web.Emails;

/// <summary>
/// Общие настройки HTTP-ориентированных провайдеров временной почты.
/// </summary>
public class HttpTemporaryEmailProviderOptions : TemporaryEmailProviderOptions
{
    /// <summary>
    /// Базовый URL upstream API.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;
}