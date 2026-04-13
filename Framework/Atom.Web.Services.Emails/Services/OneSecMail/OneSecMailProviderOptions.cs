namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки провайдера 1SecMail-подобного query API.
/// </summary>
public sealed class OneSecMailProviderOptions : HttpTemporaryEmailProviderOptions
{
    /// <summary>
    /// Инициализирует настройки провайдера со значениями по умолчанию.
    /// </summary>
    public OneSecMailProviderOptions()
    {
        BaseUrl = OneSecMailProvider.DefaultApiUrl;
    }

    /// <summary>
    /// Резервный набор доменов, если upstream не вернул список доменов.
    /// </summary>
    public string[] FallbackDomains { get; init; } = ["1secmail.com", "1secmail.org", "1secmail.net"];
}