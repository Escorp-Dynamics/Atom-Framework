namespace Atom.Web.Emails;

/// <summary>
/// Общие настройки провайдеров с локально фиксированным набором поддерживаемых доменов.
/// </summary>
public class FixedDomainTemporaryEmailProviderOptions : HttpTemporaryEmailProviderOptions
{
    /// <summary>
    /// Локально известный список доменов, из которого провайдер выбирает адрес.
    /// </summary>
    public string[] SupportedDomains { get; init; } = [];
}