namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки resource-path провайдера temp-mail.org.
/// </summary>
public sealed class TempMailOrgProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public TempMailOrgProviderOptions()
    {
        BaseUrl = TempMailOrgProvider.DefaultApiUrl;
        SupportedDomains = ["temp-mail.org"];
    }
}