namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Tempail.
/// </summary>
public sealed class TempailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public TempailProviderOptions()
    {
        BaseUrl = TempailProvider.DefaultApiUrl;
        SupportedDomains = ["necub.com"];
    }
}