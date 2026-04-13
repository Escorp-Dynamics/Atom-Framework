namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки JSON-провайдера Emailnator.
/// </summary>
public sealed class EmailnatorProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public EmailnatorProviderOptions()
    {
        BaseUrl = EmailnatorProvider.DefaultApiUrl;
        SupportedDomains = ["emailnator.com"];
    }
}