namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки REST-провайдера temp-mail.io.
/// </summary>
public sealed class TempMailIoProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public TempMailIoProviderOptions()
    {
        BaseUrl = TempMailIoProvider.DefaultApiUrl;
        SupportedDomains = ["temp-mail.io"];
    }
}