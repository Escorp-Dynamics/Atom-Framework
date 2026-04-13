namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки провайдера mail.gw.
/// </summary>
public sealed class MailGwProviderOptions : HttpTemporaryEmailProviderOptions
{
    /// <summary>
    /// Инициализирует настройки mail.gw со значениями по умолчанию.
    /// </summary>
    public MailGwProviderOptions()
    {
        BaseUrl = MailGwProvider.DefaultApiUrl;
    }
}