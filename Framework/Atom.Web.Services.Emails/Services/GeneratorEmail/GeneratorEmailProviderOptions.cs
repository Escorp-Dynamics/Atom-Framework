namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера generator.email.
/// </summary>
public sealed class GeneratorEmailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public GeneratorEmailProviderOptions()
    {
        BaseUrl = GeneratorEmailProvider.DefaultApiUrl;
        SupportedDomains = ["mail-temp.com"];
    }
}