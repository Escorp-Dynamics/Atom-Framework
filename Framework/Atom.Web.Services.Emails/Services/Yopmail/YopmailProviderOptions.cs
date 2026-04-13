namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки HTML-провайдера Yopmail.
/// </summary>
public sealed class YopmailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public YopmailProviderOptions()
    {
        BaseUrl = YopmailProvider.DefaultApiUrl;
        SupportedDomains = ["yopmail.com"];
    }
}