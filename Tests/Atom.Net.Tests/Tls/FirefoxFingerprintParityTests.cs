using System.Linq;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Закрепляет ClientHello профиля Firefox сверкой с настоящим браузером по JA3 и JA4.
/// </summary>
/// <remarks>
/// Эталон снят с Firefox 154 на этой машине: браузер направлялся на локальный слушатель, который
/// принимал ClientHello и не отвечал, а сообщение разбиралось тем же <see cref="ClientHelloInspector"/>,
/// что и здесь. Значения совпали целиком.
///
/// В отличие от Chrome, у Firefox проверяется ТОЧНЫЙ JA3, а не только состав: Chrome с версии 110
/// перемешивает расширения на каждое соединение и его JA3 «плавает», Firefox же порядок держит —
/// и потому здесь наблюдаема каждая перестановка. Именно так был найден настоящий промах: профиль
/// не объявлял имя сервера, оно дописывалось в конец из значений по умолчанию, и JA3 расходился
/// при полном совпадении состава, наборов шифров, групп и подписей.
///
/// Проверка офлайновая: ClientHello строится тем же кодом, что уходит в сеть, и разбирается на
/// месте — сети и живого браузера для прогона не нужно.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class FirefoxFingerprintParityTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Отпечаток JA3 настоящего Firefox 154 при обращении к «localhost».</summary>
    private const string FirefoxJa3Hash = "424f6d9c8b8928c0a0489a4f1a0f3e89";

    /// <summary>Отпечаток JA4 настоящего Firefox 154.</summary>
    private const string FirefoxJa4 = "t13d1517h2_8daaf6152771_3cbfd9057e0d";

    /// <summary>Расширения Firefox 154 в порядке отправки, вместе с именем сервера.</summary>
    private static readonly ushort[] FirefoxExtensionOrder =
    [
        0x0000, 0x0017, 0xFF01, 0x000A, 0x000B, 0x0023, 0x0010, 0x0005,
        0x0022, 0x0012, 0x0033, 0x002B, 0x000D, 0x002D, 0x001C, 0x001B, 0xFE0D,
    ];

    [Test]
    public void Ja3MatchesRealFirefox()
    {
        var inspector = InspectFirefoxClientHello();

        Assert.That(inspector.ComputeJa3Hash(), Is.EqualTo(FirefoxJa3Hash), inspector.ComputeJa3());
    }

    [Test]
    public void Ja4MatchesRealFirefox()
    {
        var inspector = InspectFirefoxClientHello();

        Assert.That(inspector.ComputeJa4(), Is.EqualTo(FirefoxJa4));
    }

    [Test]
    public void ExtensionOrderMatchesRealFirefox()
    {
        var inspector = InspectFirefoxClientHello();

        Assert.That(inspector.Extensions, Is.EqualTo(FirefoxExtensionOrder).AsCollection);
    }

    [Test]
    public void SessionIdIsAlwaysPresent()
    {
        // Firefox отправляет тридцатидвухбайтовый идентификатор сессии ВСЕГДА, даже когда
        // возобновлять нечего: это наследие маскировки под TLS 1.2, и пустое поле здесь заметно.
        var inspector = InspectFirefoxClientHello();

        Assert.That(inspector.SessionIdLength, Is.EqualTo(32));
    }

    /// <summary>
    /// Строит ClientHello профилем Firefox и разбирает его.
    /// </summary>
    /// <returns>Разобранное сообщение.</returns>
    private static ClientHelloInspector InspectFirefoxClientHello()
    {
        var profile = BrowserProfileCatalog.CreateFirefoxDesktop();
        var settings = profile.Tls with { Extensions = [.. profile.Tls.Extensions.Select(WithHostName)] };

        using var handshake = new Tls13ClientHandshake(settings);

        return ClientHelloInspector.Parse(handshake.BuildClientHello());
    }

    /// <summary>
    /// Подставляет имя сервера, которое в профиле оставлено пустым.
    /// </summary>
    /// <param name="extension">Расширение профиля.</param>
    /// <returns>Расширение, готовое к отправке.</returns>
    /// <remarks>
    /// Профиль описывает ПОРЯДОК и состав, а конкретное имя известно только на подключении.
    /// Подстановка на месте, а не добавление в конец, — ровно то поведение, которое проверяется.
    /// </remarks>
    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "localhost" }
            : extension;
}
