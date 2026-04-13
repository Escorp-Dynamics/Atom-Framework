namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки XML-провайдера Mailnesia.
/// </summary>
public sealed class MailnesiaProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MailnesiaProviderOptions()
    {
        BaseUrl = MailnesiaProvider.DefaultApiUrl;
        SupportedDomains = ["mailnesia.com"];
    }
}