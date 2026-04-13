namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера MinuteInbox.
/// </summary>
public sealed class MinuteInboxProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MinuteInboxProviderOptions()
    {
        BaseUrl = MinuteInboxProvider.DefaultApiUrl;
        SupportedDomains = ["minuteinbox.com"];
    }
}