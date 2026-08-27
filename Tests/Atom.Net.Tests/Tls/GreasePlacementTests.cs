using System.Linq;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет расстановку подставных значений GREASE в ClientHello.
/// </summary>
/// <remarks>
/// ★ Слепое пятно, из-за которого три ошибки прожили дольше всех остальных: НИ ja3, НИ ja4 не
/// показывают GREASE — оба выбрасывают его перед подсчётом. Отпечатки совпадали с настоящими
/// браузерами побайтно, а сырые байты отличались.
///
/// Найдено сверкой собственного ClientHello, снятого локальным приёмником:
/// подставной набор шифров стоял ВТОРЫМ вместо первого; в списке групп его не было вовсе, хотя в
/// долях ключа он был; а признак, включающий его в группах, терялся при пересоздании расширения
/// на слое соединений.
///
/// Правило, которому всё это подчиняется, задано библиотекой браузера: значение выбирается по
/// НАЗНАЧЕНИЮ поля, поэтому подставная группа в <c>supported_groups</c> и в <c>key_share</c>
/// обязана быть ОДНОЙ И ТОЙ ЖЕ, а два подставных расширения — РАЗНЫМИ.
///
/// Firefox GREASE не использует вовсе, и это тоже проверяется: добавить его туда — такое же
/// расхождение, как убрать у остальных.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class GreasePlacementTests
{
    private const int TestTimeoutMs = 30000;

    [TestCase("chrome")]
    [TestCase("safari")]
    public void GreaseCipherSuiteComesFirst(string browser)
    {
        var suites = Inspect(browser).CipherSuites;

        Assert.That(IsGrease(suites[0]), Is.True, $"первый набор обязан быть подставным, а это 0x{suites[0]:X4}");
        Assert.That(suites.Skip(1).Count(IsGrease), Is.Zero, "подставной набор ровно один");
    }

    [TestCase("chrome")]
    [TestCase("safari")]
    public void GreaseGroupIsTheSameInGroupsAndKeyShares(string browser)
    {
        // ★ Ровно то, что разъехалось: в долях ключа подставная группа была, в списке групп — нет.
        var hello = Inspect(browser);

        var inGroups = hello.SupportedGroups.Where(IsGrease).ToArray();
        var inShares = hello.KeyShareGroups.Where(IsGrease).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(inGroups, Has.Length.EqualTo(1), "подставная группа обязана быть в списке групп");
            Assert.That(inShares, Has.Length.EqualTo(1), "и в долях ключа");
            Assert.That(inGroups[0], Is.EqualTo(inShares[0]), "и это обязано быть одно и то же значение");
            Assert.That(hello.SupportedGroups[0], Is.EqualTo(inGroups[0]), "и стоять первой");
            Assert.That(hello.KeyShareGroups[0], Is.EqualTo(inShares[0]));
        });
    }

    [TestCase("chrome")]
    [TestCase("safari")]
    public void TwoGreaseExtensionsDifferFromEachOther(string browser)
    {
        // Два подставных расширения берут значения по РАЗНЫМ назначениям и обязаны различаться:
        // одинаковая пара опознаётся сама по себе.
        var grease = Inspect(browser).Extensions.Where(IsGrease).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(grease, Has.Length.EqualTo(2));
            Assert.That(grease[0], Is.Not.EqualTo(grease[1]));
        });
    }

    [Test]
    public void FirefoxUsesNoGreaseAtAll()
    {
        var hello = Inspect("firefox");

        Assert.Multiple(() =>
        {
            Assert.That(hello.CipherSuites.Any(IsGrease), Is.False);
            Assert.That(hello.Extensions.Any(IsGrease), Is.False);
            Assert.That(hello.SupportedGroups.Any(IsGrease), Is.False);
            Assert.That(hello.KeyShareGroups.Any(IsGrease), Is.False);
        });
    }

    /// <summary>
    /// Определяет, является ли значение подставным.
    /// </summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если это GREASE.</returns>
    /// <remarks>
    /// Ряд задан RFC 8701: оба байта одинаковы, младшая половина каждого равна десяти.
    /// </remarks>
    private static bool IsGrease(ushort value)
        => (value & 0x0F0F) is 0x0A0A && (value >> 8) == (value & 0xFF);

    private static ClientHelloInspector Inspect(string browser)
    {
        var profile = browser switch
        {
            "safari" => BrowserProfileCatalog.CreateSafariDesktopMacOs(),
            "firefox" => BrowserProfileCatalog.CreateFirefoxDesktop(),
            _ => BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(),
        };

        var settings = profile.Tls with { Extensions = [.. profile.Tls.Extensions.Select(WithHostName)] };

        using var handshake = new Tls13ClientHandshake(settings);

        return ClientHelloInspector.Parse(handshake.BuildClientHello());
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "example.com" }
            : extension;
}
