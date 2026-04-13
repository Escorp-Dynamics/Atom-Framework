namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки form-url-encoded провайдера EmailOnDeck.
/// </summary>
public sealed class EmailOnDeckProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public EmailOnDeckProviderOptions()
    {
        BaseUrl = EmailOnDeckProvider.DefaultApiUrl;
        SupportedDomains = ["emailondeck.com"];
    }
}