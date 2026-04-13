namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера MailinatorPublic.
/// </summary>
public sealed class MailinatorPublicProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MailinatorPublicProviderOptions()
    {
        BaseUrl = MailinatorPublicProvider.DefaultApiUrl;
        SupportedDomains = ["mailinator.com"];
    }
}