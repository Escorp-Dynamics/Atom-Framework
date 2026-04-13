namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера Fake Mail Generator.
/// </summary>
public sealed class FakeMailGeneratorProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public FakeMailGeneratorProviderOptions()
    {
        BaseUrl = FakeMailGeneratorProvider.DefaultApiUrl;
        SupportedDomains = ["cuvox.de"];
    }
}