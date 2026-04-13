namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Spambox.
/// </summary>
public sealed class SpamboxProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public SpamboxProviderOptions()
    {
        BaseUrl = SpamboxProvider.DefaultApiUrl;
        SupportedDomains = ["spambox.xyz"];
    }
}