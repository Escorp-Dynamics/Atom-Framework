namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки HTML-провайдера Mohmal.
/// </summary>
public sealed class MohmalProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public MohmalProviderOptions()
    {
        BaseUrl = MohmalProvider.DefaultApiUrl;
        SupportedDomains = ["mohmal.com"];
    }
}