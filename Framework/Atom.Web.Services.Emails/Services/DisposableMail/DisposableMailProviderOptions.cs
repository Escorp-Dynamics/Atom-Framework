namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера DisposableMail.
/// </summary>
public sealed class DisposableMailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public DisposableMailProviderOptions()
    {
        BaseUrl = DisposableMailProvider.DefaultApiUrl;
        SupportedDomains = ["disposablemail.com"];
    }
}