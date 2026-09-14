using Atom.Net.Https.Headers;
using Atom.Net.Https.Http;
using Atom.Net.Https.Http2;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Закрепляет способность профилей каталога сравниваться, не падая.
/// </summary>
/// <remarks>
/// Повод вполне конкретный. Наборы <see cref="TlsSettings.CipherSuites"/>,
/// <see cref="TlsSettings.Extensions"/> и <see cref="Http2Settings.PriorityTree"/> объявлены
/// инициализаторами <c lang="text">= []</c>, а те работают ТОЛЬКО через конструктор: у
/// <see langword="default"/> обеих структур поля остаются null. Сравнение вызывало
/// <c lang="text">Equals</c> на экземпляре такого поля, и <see cref="BrowserProfile"/> ронял
/// NullReferenceException на ЛЮБОМ профиле каталога — включая сравнение профиля с самим собой.
///
/// Заметить это было неоткуда: проверки отпечатка читают состав расширений и порядок настроек и
/// сравнением профилей не пользуются вовсе. Тесты ниже закрывают ровно эту дыру.
/// </remarks>
[TestFixture]
public sealed class BrowserProfileEqualityTests
{
    /// <summary>Представители всех семейств движков, включая мобильные.</summary>
    private static IEnumerable<TestCaseData> RepresentativeProfiles()
    {
        yield return new TestCaseData("Chrome Desktop Linux", BrowserProfileCatalog.CreateChromeDesktopLinux()).SetName("{m}(ChromeDesktopLinux)");
        yield return new TestCaseData("Chrome Desktop Windows TLS 1.3", BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()).SetName("{m}(ChromeDesktopWindowsTls13)");
        yield return new TestCaseData("Firefox Desktop", BrowserProfileCatalog.CreateFirefoxDesktop()).SetName("{m}(FirefoxDesktop)");
        yield return new TestCaseData("Safari Desktop macOS", BrowserProfileCatalog.CreateSafariDesktopMacOs()).SetName("{m}(SafariDesktopMacOs)");
        yield return new TestCaseData("Chrome Android", BrowserProfileCatalog.CreateChromeAndroid()).SetName("{m}(ChromeAndroid)");
        yield return new TestCaseData("Firefox Android", BrowserProfileCatalog.CreateFirefoxAndroid()).SetName("{m}(FirefoxAndroid)");
        yield return new TestCaseData("Safari iOS", BrowserProfileCatalog.CreateSafariIos()).SetName("{m}(SafariIos)");
        yield return new TestCaseData("Chrome iOS", BrowserProfileCatalog.CreateChromeIos()).SetName("{m}(ChromeIos)");
    }

    [TestCaseSource(nameof(RepresentativeProfiles))]
    public void ProfileEqualsItselfWithoutThrowing(string name, BrowserProfile profile)
    {
        // Именно это и падало: сравнение профиля с самим собой доходит до вложенных TlsSettings и
        // Http2Settings и упирается в их наборы.
        Assert.That(() => { _ = profile.Equals(profile); }, Throws.Nothing, $"{name}: самосравнение профиля не должно падать");
        Assert.That(profile.Equals(profile), Is.True, $"{name}: профиль обязан быть равен самому себе");
    }

    [TestCaseSource(nameof(RepresentativeProfiles))]
    public void ProfileHashCodeDoesNotThrow(string name, BrowserProfile profile)
    {
        Assert.That(() => { _ = profile.GetHashCode(); }, Throws.Nothing, $"{name}: подсчёт хэша профиля не должен падать");
    }

    [Test]
    public void DifferentCatalogProfilesCompareAsUnequal()
    {
        var chrome = BrowserProfileCatalog.CreateChromeDesktopLinux();
        var firefox = BrowserProfileCatalog.CreateFirefoxDesktop();

        Assert.Multiple(() =>
        {
            Assert.That(() => { _ = chrome.Equals(firefox); }, Throws.Nothing, "сравнение разных профилей не должно падать");
            Assert.That(chrome.Equals(firefox), Is.False, "профили разных движков не могут быть равны");
            Assert.That(firefox.Equals(chrome), Is.False, "сравнение обязано быть симметричным");
        });
    }

    [Test]
    public void DefaultTlsSettingsComparesWithoutThrowing()
    {
        // У default наборы равны null: инициализаторы `= []` до них не доходят.
        var empty = default(TlsSettings);
        var filled = BrowserProfileCatalog.CreateChromeDesktopLinux().Tls;

        Assert.Multiple(() =>
        {
            Assert.That(() => { _ = empty.Equals(empty); }, Throws.Nothing, "самосравнение default(TlsSettings) не должно падать");
            Assert.That(empty.Equals(empty), Is.True, "default(TlsSettings) равен самому себе");
            Assert.That(() => { _ = empty.Equals(filled); }, Throws.Nothing, "сравнение default с заполненным не должно падать");
            Assert.That(empty.Equals(filled), Is.False, "пустые настройки не равны настройкам профиля");
            Assert.That(filled.Equals(empty), Is.False, "сравнение обязано быть симметричным");
            Assert.That(() => { _ = empty.GetHashCode(); }, Throws.Nothing, "хэш default(TlsSettings) не должен падать");
        });
    }

    [Test]
    public void DefaultHttp2SettingsComparesWithoutThrowing()
    {
        // То же самое и здесь: PriorityTree у default остаётся null.
        var empty = default(Http2Settings);
        var filled = Http2ProfileCatalog.CreateChrome();

        Assert.Multiple(() =>
        {
            Assert.That(() => { _ = empty.Equals(empty); }, Throws.Nothing, "самосравнение default(Http2Settings) не должно падать");
            Assert.That(empty.Equals(empty), Is.True, "default(Http2Settings) равен самому себе");
            Assert.That(() => { _ = empty.Equals(filled); }, Throws.Nothing, "сравнение default с заполненным не должно падать");
            Assert.That(empty.Equals(filled), Is.False, "пустые настройки не равны настройкам профиля");
            Assert.That(filled.Equals(empty), Is.False, "сравнение обязано быть симметричным");
            Assert.That(() => { _ = empty.GetHashCode(); }, Throws.Nothing, "хэш default(Http2Settings) не должен падать");
            Assert.That(() => { _ = filled.GetHashCode(); }, Throws.Nothing, "хэш настроек профиля не должен падать");
        });
    }

    [Test]
    public void DefaultUserAgentStructuresCompareWithoutThrowing()
    {
        // Строковые поля и наборы у default(OsInfo)/default(Versioned) тоже null: сравнение профилей
        // доходит сюда через UserAgent.Equals, и раньше падало именно на этих структурах.
        var emptyOs = default(OsInfo);
        var emptyVersioned = default(Versioned);
        var emptyAgent = default(UserAgent);

        Assert.Multiple(() =>
        {
            Assert.That(() => { _ = emptyOs.Equals(emptyOs); }, Throws.Nothing, "самосравнение default(OsInfo) не должно падать");
            Assert.That(emptyOs.Equals(emptyOs), Is.True, "default(OsInfo) равен самому себе");
            Assert.That(() => { _ = emptyOs.GetHashCode(); }, Throws.Nothing, "хэш default(OsInfo) не должен падать");
            Assert.That(() => { _ = emptyVersioned.Equals(emptyVersioned); }, Throws.Nothing, "самосравнение default(Versioned) не должно падать");
            Assert.That(emptyVersioned.Equals(emptyVersioned), Is.True, "default(Versioned) равен самому себе");
            Assert.That(() => { _ = emptyVersioned.GetHashCode(); }, Throws.Nothing, "хэш default(Versioned) не должен падать");
            Assert.That(() => { _ = emptyAgent.Equals(emptyAgent); }, Throws.Nothing, "самосравнение default(UserAgent) не должно падать");
            Assert.That(() => { _ = emptyAgent.GetHashCode(); }, Throws.Nothing, "хэш default(UserAgent) не должен падать");
        });
    }

    [Test]
    public void NestedDefaultTlsInsideHttp2DoesNotBreakComparison()
    {
        // Ровно тот путь, которым дефект доходил до профиля: Http2 профиля никто не задаёт TLS,
        // поэтому вложенный Http2Settings.Tls — это default со своими null-наборами.
        var chrome = Http2ProfileCatalog.CreateChrome();
        var firefox = Http2ProfileCatalog.CreateFirefox();

        Assert.Multiple(() =>
        {
            Assert.That(chrome.Tls.CipherSuites, Is.Null, "предпосылка теста: вложенный TLS остаётся незаполненным");
            Assert.That(() => { _ = chrome.Equals(firefox); }, Throws.Nothing, "сравнение настроек HTTP/2 не должно падать");
            Assert.That(chrome.Equals(firefox), Is.False, "наборы Chrome и Firefox различаются");
            Assert.That(chrome.Equals(chrome), Is.True, "набор равен самому себе");
        });
    }

    [Test]
    public void ResolvedProfilesCompareWithoutThrowing()
    {
        // Второй вход в тот же код: приложения берут профиль резолвером, а не фабрикой напрямую.
        var chrome = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        var firefox = BrowserProfileResolver.Resolve("Mozilla/5.0 (X11; Linux x86_64; rv:154.0) Gecko/20100101 Firefox/154.0");
        var android = BrowserProfileResolver.Resolve("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36");

        Assert.Multiple(() =>
        {
            Assert.That(chrome.Equals(chrome), Is.True, "подобранный профиль равен самому себе");
            Assert.That(chrome.Equals(firefox), Is.False, "подобранные профили разных движков не равны");
            Assert.That(android.Equals(chrome), Is.False, "мобильный профиль не равен настольному");
            Assert.That(() => { _ = android.GetHashCode(); }, Throws.Nothing, "хэш подобранного профиля не должен падать");
        });
    }
}
