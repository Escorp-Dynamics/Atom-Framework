using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using Atom.Net.Https.Profiles;
using Atom.Net.Quic;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Закрепляет перестановку расширений и форму рукопожатия поверх QUIC.
/// </summary>
/// <remarks>
/// ★ Оба свойства найдены собственным замером и оба опровергают то, что считалось верным раньше.
///
/// Первое: настоящий Chrome отправляет расширения в СЛУЧАЙНОМ порядке, заново на каждое
/// соединение. Три подряд подключения одного браузера к одному узлу дали три разных ja3
/// (<c>d975c2e0…</c>, <c>caa73154…</c>, <c>1ce6e30d…</c>). Значит совпадение с «эталонным» хэшем
/// Chrome ничего не подтверждает, а ПОСТОЯНСТВО хэша выдаёт подделку.
///
/// Второе: рукопожатие поверх QUIC отличается от обычного далеко не только требованиями
/// транспорта. Замер снят приёмником, расшифровывающим начальный пакет QUIC (его защита выводится
/// из идентификатора соединения, который едет открытым текстом):
///
/// <code>
/// Chrome  QUIC:  3 набора шифров, 11 расширений, GREASE нет вовсе,        4 группы
/// Firefox QUIC:  3 набора шифров, 15 расширений, два последних закреплены, 5 групп
/// </code>
///
/// Проверяется здесь СОСТАВ и правила закрепления — то, что у браузера постоянно. Порядок
/// проверяется лишь на изменчивость.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class ExtensionPermutationTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Расширения Chrome поверх QUIC, снятые с браузера.</summary>
    private static readonly ushort[] ChromeQuicExtensions =
    [
        0x0000, 0x0033, 0x0039, 0x002D, 0x000A, 0x002B, 0x001B, 0x44CD, 0x000D, 0x0010, 0xFE0D,
    ];

    /// <summary>Расширения Firefox поверх QUIC, снятые с браузера.</summary>
    private static readonly ushort[] FirefoxQuicExtensions =
    [
        0xFF01, 0x000A, 0x0000, 0x002D, 0x0010, 0x001C, 0x0033, 0x000D,
        0x0017, 0x002B, 0x0005, 0x001B, 0x0022, 0x0039, 0xFE0D,
    ];

    [Test]
    public void ChromeSendsADifferentOrderEveryHandshake()
    {
        // Восьми построений хватает с запасом: шестнадцать переставляемых расширений дают
        // столько сочетаний, что совпадение всех восьми означало бы неработающую перестановку.
        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 8; attempt++) orders.Add(string.Join(",", Inspect(Chrome()).Extensions));

        Assert.That(orders, Has.Count.GreaterThan(1), "порядок обязан меняться от соединения к соединению");
    }

    [Test]
    public void ChromeKeepsTheSameExtensionSetWhileShuffling()
    {
        // Меняется ПОРЯДОК, но не состав: лишнее или пропавшее расширение видно сразу. Подставные
        // значения из сравнения исключены — они и обязаны быть разными в каждом соединении.
        var sets = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 8; attempt++) sets.Add(string.Join(",", Inspect(Chrome()).Extensions.Where(static id => !IsGrease(id)).Order()));

        Assert.That(sets, Has.Count.EqualTo(1), "состав обязан остаться неизменным");
    }

    [Test]
    public void GreaseStaysAtBothEndsWhileTheRestMoves()
    {
        // ★ Подставные значения перестановка не двигает: у браузера они всегда обрамляют список,
        // и подставное расширение посреди сообщения — само по себе расхождение.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var ids = Inspect(Chrome()).Extensions;

            Assert.Multiple(() =>
            {
                Assert.That(IsGrease(ids[0]), Is.True, $"первым обязано стоять подставное, а это 0x{ids[0]:X4}");
                Assert.That(IsGrease(ids[^1]), Is.True, $"последним тоже, а это 0x{ids[^1]:X4}");
                Assert.That(ids.Skip(1).Take(ids.Count - 2).Any(IsGrease), Is.False, "и больше нигде");
            });
        }
    }

    [Test]
    public void FirefoxKeepsAFixedOrderOverTcp()
    {
        // Расхождение Firefox с самим собой: поверх TCP порядок ПОСТОЯНЕН — перестановка в его
        // библиотеке выключена по умолчанию, — а поверх QUIC меняется, потому что её включает
        // стек QUIC. Снято замером: два подключения поверх TCP дали один и тот же порядок.
        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 4; attempt++) orders.Add(string.Join(",", Inspect(BrowserProfileCatalog.CreateFirefoxDesktop().Tls).Extensions));

        Assert.That(orders, Has.Count.EqualTo(1), "поверх TCP Firefox порядок не меняет");
    }

    [Test]
    public void QuicOffersOnlyTls13CipherSuites()
    {
        // QUIC определён только поверх TLS 1.3 (RFC 9001, §4.2), и браузеры наборы прежних
        // версий не предлагают: пятнадцать превращаются в три.
        Assert.Multiple(() =>
        {
            Assert.That(Inspect(ChromeQuic()).CipherSuites, Has.Count.EqualTo(3));
            Assert.That(Inspect(FirefoxQuic()).CipherSuites, Has.Count.EqualTo(3));
            Assert.That(Inspect(ChromeQuic()).CipherSuites.All(static suite => suite is >= 0x1301 and <= 0x1305), Is.True);
        });
    }

    [Test]
    public void QuicCarriesNoGreaseAtAll()
    {
        // ★ Против ожидания: поверх QUIC движки Chromium не ставят подставных значений НИГДЕ —
        // ни в наборах шифров, ни в расширениях, ни в группах. Их роль там играет транспорт:
        // подставная версия в version_information и подставный параметр транспорта.
        var hello = Inspect(ChromeQuic());

        Assert.Multiple(() =>
        {
            Assert.That(hello.CipherSuites.Any(IsGrease), Is.False, "подставного набора шифров быть не должно");
            Assert.That(hello.Extensions.Any(IsGrease), Is.False, "подставных расширений тоже");
            Assert.That(hello.SupportedGroups.Any(IsGrease), Is.False, "и подставной группы");
        });
    }

    [Test]
    public void ChromeQuicExtensionSetMatchesTheCapturedBrowser()
    {
        Assert.That(Inspect(ChromeQuic()).Extensions.Order(), Is.EqualTo(ChromeQuicExtensions.Order()).AsCollection);
    }

    [Test]
    public void FirefoxQuicExtensionSetMatchesTheCapturedBrowser()
    {
        // Firefox сохраняет поверх QUIC то, что движки Chromium убирают: расширенный секрет,
        // признак перезаключения и запрос состояния сертификата. Разница в библиотеках, не в
        // протоколе, и вывести её из спецификации нельзя — только замером.
        Assert.That(Inspect(FirefoxQuic()).Extensions.Order(), Is.EqualTo(FirefoxQuicExtensions.Order()).AsCollection);
    }

    [Test]
    public void FirefoxQuicKeepsTransportParametersAndEchLast()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var ids = Inspect(FirefoxQuic()).Extensions;

            Assert.Multiple(() =>
            {
                Assert.That(ids[^2], Is.EqualTo(QuicTransportParameters.ExtensionId), "параметры транспорта — предпоследние");
                Assert.That(ids[^1], Is.EqualTo((ushort)0xFE0D), "ECH замыкает сообщение");
            });
        }
    }

    [Test]
    public void FirefoxDropsFiniteFieldGroupsOverQuic()
    {
        // Поверх TCP Firefox предлагает семь групп, включая FFDHE; поверх QUIC — пять.
        var overTcp = Inspect(BrowserProfileCatalog.CreateFirefoxDesktop().Tls).SupportedGroups;
        var overQuic = Inspect(FirefoxQuic()).SupportedGroups;

        Assert.Multiple(() =>
        {
            Assert.That(overTcp.Any(static group => group is >= 0x0100 and <= 0x0104), Is.True);
            Assert.That(overQuic.Any(static group => group is >= 0x0100 and <= 0x0104), Is.False);
            Assert.That(overQuic, Has.Count.EqualTo(5));
        });
    }

    [Test]
    public void HelloOfferingOnlyTls13OmitsTheSignallingCipherSuite()
    {
        // ★ Сигнальный набор 0x00FF заменяет расширение renegotiation_info (RFC 5746), и при его
        // отсутствии добавлялся сам. Поверх QUIC движки Chromium это расширение убирают — и
        // сигнальный набор тут же давал ЧЕТВЁРТЫЙ набор шифров там, где браузер шлёт три.
        // Пересогласования в TLS 1.3 нет вовсе, поэтому не нужно ни то, ни другое.
        var hello = Inspect(ChromeQuic());

        Assert.Multiple(() =>
        {
            Assert.That(hello.Extensions, Does.Not.Contain((ushort)0xFF01));
            Assert.That(hello.CipherSuites, Does.Not.Contain((ushort)0x00FF));
        });
    }

    /// <summary>Профиль Chrome поверх TCP.</summary>
    /// <returns>Настройки рукопожатия.</returns>
    private static TlsSettings Chrome() => BrowserProfileCatalog.CreateChromeDesktopWindowsTls13().Tls;

    /// <summary>Профиль Chrome в том виде, в каком он уходит поверх QUIC.</summary>
    /// <returns>Настройки рукопожатия.</returns>
    private static TlsSettings ChromeQuic()
        => QuicTlsShaping.Apply(Chrome(), QuicTransportProfile.Chromium, "example.com", QuicParameters());

    /// <summary>Профиль Firefox в том виде, в каком он уходит поверх QUIC.</summary>
    /// <returns>Настройки рукопожатия.</returns>
    private static TlsSettings FirefoxQuic()
        => QuicTlsShaping.Apply(BrowserProfileCatalog.CreateFirefoxDesktop().Tls, QuicTransportProfile.Firefox, "example.com", QuicParameters());

    /// <summary>Закодированные параметры транспорта для расширения 0x0039.</summary>
    /// <returns>Тело расширения.</returns>
    private static ReadOnlyMemory<byte> QuicParameters()
    {
        var buffer = new byte[512];
        var written = QuicTransportParameters.CreateFirefox().Write(buffer);

        return buffer.AsMemory(0, written);
    }

    /// <summary>Строит ClientHello и разбирает его.</summary>
    /// <param name="settings">Настройки рукопожатия.</param>
    /// <returns>Разобранное сообщение.</returns>
    private static ClientHelloInspector Inspect(TlsSettings settings)
    {
        var prepared = settings with { Extensions = [.. settings.Extensions.Select(WithHostName)] };

        using var handshake = new Tls13ClientHandshake(prepared);

        return ClientHelloInspector.Parse(handshake.BuildClientHello());
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension { HostName: null or "" } serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "example.com" }
            : extension;

    /// <summary>Определяет, является ли значение подставным (RFC 8701).</summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если это GREASE.</returns>
    private static bool IsGrease(ushort value)
        => (value & 0x0F0F) is 0x0A0A && (value >> 8) == (value & 0xFF);
}
