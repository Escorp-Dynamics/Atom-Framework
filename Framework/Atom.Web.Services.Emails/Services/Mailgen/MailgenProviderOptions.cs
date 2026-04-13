namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Mailgen.
/// </summary>
public sealed class MailgenProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MailgenProviderOptions()
    {
        BaseUrl = MailgenProvider.DefaultApiUrl;
        SupportedDomains = ["mailgen.biz"];
    }
}