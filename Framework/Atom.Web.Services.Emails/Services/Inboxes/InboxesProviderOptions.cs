namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера inboxes.com.
/// </summary>
public sealed class InboxesProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public InboxesProviderOptions()
    {
        BaseUrl = InboxesProvider.DefaultApiUrl;
        SupportedDomains = ["inboxes.com"];
    }
}