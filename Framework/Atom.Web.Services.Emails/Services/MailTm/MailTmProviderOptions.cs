namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки concrete provider для upstream API mail.tm.
/// </summary>
public sealed class MailTmProviderOptions : HttpTemporaryEmailProviderOptions
{
    /// <summary>
    /// Инициализирует настройки mail.tm дефолтным BaseUrl.
    /// </summary>
    public MailTmProviderOptions()
    {
        BaseUrl = MailTmProvider.DefaultApiUrl;
    }
}