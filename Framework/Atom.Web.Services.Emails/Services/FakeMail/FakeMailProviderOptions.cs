namespace Atom.Web.Emails.Services;

/// <summary>
/// Настройки публичного no-registration провайдера FakeMail.
/// </summary>
public sealed class FakeMailProviderOptions : FixedDomainTemporaryEmailProviderOptions
{
    /// <summary>
    /// Создаёт настройки провайдера со значениями по умолчанию.
    /// </summary>
    public FakeMailProviderOptions()
    {
        BaseUrl = FakeMailProvider.DefaultApiUrl;
        SupportedDomains = ["fakemail.net"];
    }
}