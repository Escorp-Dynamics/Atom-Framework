namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера MailCatch.
/// </summary>
public sealed class MailCatchProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MailCatchProviderOptions()
    {
        BaseUrl = MailCatchProvider.DefaultApiUrl;
        SupportedDomains = ["mailcatch.com"];
    }
}