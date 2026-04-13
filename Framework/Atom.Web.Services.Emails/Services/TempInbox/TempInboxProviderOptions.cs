namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера TempInbox.
/// </summary>
public sealed class TempInboxProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public TempInboxProviderOptions()
    {
        BaseUrl = TempInboxProvider.DefaultApiUrl;
        SupportedDomains = ["tempinbox.com"];
    }
}