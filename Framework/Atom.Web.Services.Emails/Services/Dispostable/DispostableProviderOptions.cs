namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки plain-text провайдера Dispostable.
/// </summary>
public sealed class DispostableProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public DispostableProviderOptions()
    {
        BaseUrl = DispostableProvider.DefaultApiUrl;
        SupportedDomains = ["dispostable.com"];
    }
}