namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки провайдера GuerrillaMail.
/// </summary>
public sealed class GuerrillaMailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Инициализирует настройки провайдера со значениями по умолчанию.
    /// </summary>
    public GuerrillaMailProviderOptions()
    {
        BaseUrl = GuerrillaMailProvider.DefaultApiUrl;
        SupportedDomains = ["sharklasers.com", "guerrillamail.com", "guerrillamail.net", "guerrillamailblock.com", "grr.la"];
    }
}