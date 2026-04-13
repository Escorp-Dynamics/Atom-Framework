namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера emailfake.com.
/// </summary>
public sealed class EmailFakeProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public EmailFakeProviderOptions()
    {
        BaseUrl = EmailFakeProvider.DefaultApiUrl;
        SupportedDomains = ["adsensekorea.com"];
    }
}