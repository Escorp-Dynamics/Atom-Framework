namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Moakt.
/// </summary>
public sealed class MoaktProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MoaktProviderOptions()
    {
        BaseUrl = MoaktProvider.DefaultApiUrl;
        SupportedDomains = ["moakt.cc"];
    }
}