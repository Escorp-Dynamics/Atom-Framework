using System.Linq;
using System.Reflection;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет свойства, обязательные для ЛЮБОГО профиля каталога, включая ещё не написанные.
/// </summary>
/// <remarks>
/// Обычные проверки отпечатка закрепляют конкретный браузер, и новый профиль они не увидят. Эти
/// же обходят каталог отражением, поэтому любой добавленный профиль попадает под них сам.
///
/// Повод вполне конкретный. Профиль Firefox не объявлял имя сервера, и оно дописывалось в конец
/// из значений по умолчанию — состав расширений при этом совпадал с браузером полностью, а
/// порядок расходился, и вместе с ним расходился JA3. Никакой отказ на это не указывал:
/// соединение работало. Проверка ниже делает такую ошибку невозможной для всех профилей сразу.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class BrowserProfileCatalogInvariantTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Все профили каталога, создаваемые без аргументов.</summary>
    private static IEnumerable<TestCaseData> AllProfiles()
        => typeof(BrowserProfileCatalog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method => method.ReturnType == typeof(BrowserProfile) && method.GetParameters().Length is 0)
            .Select(static method => new TestCaseData(method.Name, (BrowserProfile)method.Invoke(null, null)!).SetName($"{{m}}({method.Name})"));

    [TestCaseSource(nameof(AllProfiles))]
    public void ProfileDeclaresServerNameItself(string name, BrowserProfile profile)
    {
        // Имя сервера обязано стоять в профиле на своём месте: не объявив его, профиль получит
        // расширение дописанным в хвост, и порядок разойдётся с браузером.
        var hasServerName = profile.Tls.Extensions.Any(static extension => extension is ServerNameTlsExtension);

        Assert.That(hasServerName, Is.True, $"{name}: профиль должен объявлять имя сервера сам");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void ProfileOffersAlpn(string name, BrowserProfile profile)
    {
        // То же и с ALPN: без него сервер не предложит HTTP/2, а браузер его предлагает всегда.
        var alpn = profile.Tls.Extensions.OfType<AlpnTlsExtension>().SingleOrDefault();

        Assert.That(alpn?.Protocols, Is.Not.Null.And.Not.Empty, $"{name}: профиль должен предлагать ALPN");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void ProfileHasNoDuplicateExtensions(string name, BrowserProfile profile)
    {
        // Повторное расширение — ошибка протокола (RFC 8446, §4.2), но сервер чаще просто
        // разрывает соединение без объяснения, и искать причину приходится долго. GREASE считать
        // не нужно: его в ClientHello намеренно несколько.
        var duplicates = profile.Tls.Extensions
            .Where(static extension => extension is not GreaseTlsExtension)
            .GroupBy(static extension => extension.Id)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"0x{group.Key:X4}")
            .ToArray();

        Assert.That(duplicates, Is.Empty, $"{name}: расширения повторяются");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void ProfileDeclaresEveryEssentialExtension(string name, BrowserProfile profile)
    {
        // ★ Обязательные расширения слой соединений дописывает сам, если их нет, — и дописывает
        // В ХВОСТ. Для профиля браузера это порча отпечатка: порядок расширений наблюдаем целиком.
        // Ровно так и ломался Firefox, у которого имя сервера уезжало последним вместо первого.
        //
        // Требуя объявить их явно, мы делаем ту ветку недостижимой для профилей.
        // Версии и доля ключа сюда не входят: они нужны только TLS 1.3, а в каталоге есть и
        // профили эпохи TLS 1.2, для которых их отсутствие правильно.
        ushort[] essentials = [0x0000, 0x000A, 0x000D, 0x0010];

        var declared = profile.Tls.Extensions.Select(static extension => extension.Id).ToHashSet();
        var missing = essentials.Where(id => !declared.Contains(id)).Select(static id => $"0x{id:X4}").ToArray();

        Assert.That(missing, Is.Empty, $"{name}: профиль обязан объявлять обязательные расширения сам");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void ProfileOffersCipherSuites(string name, BrowserProfile profile)
    {
        Assert.That(profile.Tls.CipherSuites, Is.Not.Empty, $"{name}: профиль должен предлагать наборы шифров");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void MobileProfilesAnnounceThemselvesAsMobile(string name, BrowserProfile profile)
    {
        // Признак мобильности управляет подсказками клиента (sec-ch-ua-mobile) и должен
        // соответствовать строке агента: несовпадение этих двух — готовый признак подделки.
        var looksMobile = profile.UserAgent.Contains("Android", StringComparison.Ordinal)
            || profile.UserAgent.Contains("iPhone", StringComparison.Ordinal)
            || profile.UserAgent.Contains("iPad", StringComparison.Ordinal);

        Assert.That(profile.IsMobile, Is.EqualTo(looksMobile), $"{name}: признак мобильности расходится со строкой агента");
    }
}
