namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки GraphQL-провайдера DropMail.
/// </summary>
public sealed class DropMailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки DropMail со значениями по умолчанию.
    /// </summary>
    public DropMailProviderOptions()
    {
        BaseUrl = DropMailProvider.DefaultApiUrl;
        SupportedDomains = ["dropmail.me"];
    }
}