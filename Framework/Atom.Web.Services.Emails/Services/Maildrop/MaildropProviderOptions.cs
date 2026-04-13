namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Maildrop.
/// </summary>
public sealed class MaildropProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MaildropProviderOptions()
    {
        BaseUrl = MaildropProvider.DefaultApiUrl;
        SupportedDomains = ["maildrop.cc"];
    }
}