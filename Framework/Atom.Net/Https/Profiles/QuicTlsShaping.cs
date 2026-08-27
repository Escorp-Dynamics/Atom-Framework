using System.Security.Authentication;
using Atom.Net.Quic;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Приведение профиля TLS к тому виду, в каком браузер отправляет его поверх QUIC.
/// </summary>
/// <remarks>
/// ★ Прежде считалось, что рукопожатие поверх QUIC отличается от обычного лишь тем, что требует
/// сам транспорт: <c>h3</c> в ALPN, только TLS 1.3 в supported_versions, пустой идентификатор
/// сессии и расширение с параметрами транспорта. Это оказалось неверно, и расхождение было
/// крупным.
///
/// Замер снят собственным приёмником, который расшифровывает начальный пакет QUIC: его защита
/// выводится из идентификатора соединения, а тот едет открытым текстом, — поэтому первое
/// сообщение браузера читается без всякого доступа к его памяти или ключам.
///
/// <code>
/// Chrome  поверх TCP : 15 наборов шифров, 18 расширений, GREASE в шифрах, группах и списке
/// Chrome  поверх QUIC:  3 набора,        11 расширений, GREASE НЕТ ВОВСЕ
/// Firefox поверх TCP : 15 наборов,       17 расширений, 7 групп (включая FFDHE)
/// Firefox поверх QUIC:  3 набора,        15 расширений, 5 групп (FFDHE нет)
/// </code>
///
/// Отсюда четыре правила, и ни одно из них не выводится из спецификации.
///
/// Наборы шифров сокращаются до трёх: QUIC определён только поверх TLS 1.3 (RFC 9001, §4.2), и
/// предлагать наборы прежних версий незачем — браузер их и не предлагает.
///
/// Подставные значения GREASE исчезают целиком: и наборы, и расширения, и группа. Внутри QUIC
/// та же роль отдана транспорту — подставная версия в <c>version_information</c> и подставный
/// параметр транспорта, — а на уровне TLS браузер их не ставит.
///
/// Из состава уходят расширения, осмысленные только до TLS 1.3: формат точек, билет сессии,
/// список прозрачности сертификатов. Дополнение (0x0015) тоже не нужно — размер выравнивает сам
/// начальный пакет QUIC.
///
/// Дальше движки расходятся, и здесь здравый смысл подводит: Chrome убирает ЕЩЁ три —
/// расширенный секрет, признак перезаключения и запрос состояния сертификата, — а Firefox все
/// три сохраняет. Причина в библиотеках: NSS отправляет их всегда, BoringSSL связывает с TLS 1.2.
/// Firefox при этом теряет группы FFDHE, которых у Chrome нет и поверх TCP.
///
/// Порядок расширений поверх QUIC перемешивается У ОБОИХ — см.
/// <see cref="ClientHelloExtensionPermutation"/>; у Firefox два последних закреплены.
/// </remarks>
public static class QuicTlsShaping
{
    /// <summary>Имя протокола HTTP/3 для ALPN.</summary>
    private static ReadOnlyMemory<byte> Http3Protocol { get; } = "h3"u8.ToArray();

    /// <summary>Расширения, которых поверх QUIC не отправляет ни один из движков.</summary>
    private static readonly ushort[] DroppedEverywhere =
    [
        0x000B,  // ec_point_formats — формат точек существует только до TLS 1.3
        0x0023,  // session_ticket — билеты TLS 1.2; в 1.3 их заменяет NewSessionTicket
        0x0012,  // signed_certificate_timestamp
        0x0015,  // padding — размер выравнивает начальный пакет QUIC
    ];

    /// <summary>Расширения, которые дополнительно убирает BoringSSL.</summary>
    private static readonly ushort[] DroppedByChromium =
    [
        0x0017,  // extended_master_secret — понятие TLS 1.2
        0xFF01,  // renegotiation_info — перезаключения в TLS 1.3 нет
        0x0005,  // status_request
    ];

    /// <summary>Закрепляемые расширения Firefox: параметры транспорта и ECH замыкают сообщение.</summary>
    private static readonly ushort[] FirefoxAnchors = [QuicTransportParameters.ExtensionId, 0xFE0D];

    /// <summary>
    /// Приводит профиль к виду, наблюдаемому поверх QUIC.
    /// </summary>
    /// <param name="settings">Профиль браузера в том виде, в каком он уходит поверх TCP.</param>
    /// <param name="shape">Какой из движков подражается.</param>
    /// <param name="hostName">Имя узла для SNI; пусто — оставить как в профиле.</param>
    /// <param name="transportParameters">Закодированные параметры транспорта QUIC.</param>
    /// <returns>Настройки рукопожатия для QUIC.</returns>
    public static TlsSettings Apply(
        in TlsSettings settings,
        QuicTransportProfile shape,
        string? hostName = null,
        ReadOnlyMemory<byte> transportParameters = default)
    {
        var isFirefox = shape is QuicTransportProfile.Firefox;
        var extensions = new List<ITlsExtension>();
        var hasTransportParameters = false;

        foreach (var extension in settings.Extensions)
        {
            if (extension is GreaseTlsExtension) continue;
            if (Array.IndexOf(DroppedEverywhere, extension.Id) >= 0) continue;
            if (!isFirefox && Array.IndexOf(DroppedByChromium, extension.Id) >= 0) continue;

            extensions.Add(Reshape(extension, isFirefox, hostName));

            if (extension is QuicTransportParametersTlsExtension) hasTransportParameters = true;
        }

        // Параметры транспорта — предпоследние: за ними у обоих движков идёт только ECH, и
        // перестановка их с места не сдвинет.
        if (!hasTransportParameters)
        {
            var beforeEch = extensions.FindIndex(static candidate => candidate.Id is 0xFE0D);
            var transport = new QuicTransportParametersTlsExtension { Data = transportParameters };

            if (beforeEch < 0) extensions.Add(transport);
            else extensions.Insert(beforeEch, transport);
        }

        return settings with
        {
            MinVersion = SslProtocols.Tls13,
            MaxVersion = SslProtocols.Tls13,
            CipherSuites = [.. settings.CipherSuites.Where(IsTls13Suite)],
            Extensions = extensions,
            SessionIdPolicy = SessionIdPolicy.Empty,
            PermuteExtensions = true,
            PermutationAnchors = isFirefox ? FirefoxAnchors : null,
        };
    }

    /// <summary>
    /// Правит отдельное расширение под требования QUIC.
    /// </summary>
    /// <param name="extension">Исходное расширение.</param>
    /// <param name="isFirefox">Подражается ли Firefox.</param>
    /// <param name="hostName">Имя узла для SNI.</param>
    /// <returns>Расширение в том виде, в каком его отправляет браузер.</returns>
    private static ITlsExtension Reshape(ITlsExtension extension, bool isFirefox, string? hostName) => extension switch
    {
        ServerNameTlsExtension serverName when !string.IsNullOrWhiteSpace(hostName) && string.IsNullOrWhiteSpace(serverName.HostName)
            => new ServerNameTlsExtension { Id = serverName.Id, HostName = hostName },

        AlpnTlsExtension alpn => new AlpnTlsExtension { Id = alpn.Id, Protocols = [Http3Protocol] },

        SupportedVersionsTlsExtension versions => new SupportedVersionsTlsExtension { Id = versions.Id, Versions = [SslProtocols.Tls13] },

        // Группы FFDHE поверх QUIC не предлагаются: обмен ключами там всегда на эллиптических
        // кривых, и в замере Firefox их нет, хотя поверх TCP они есть.
        SupportedGroupsTlsExtension groups => new SupportedGroupsTlsExtension
        {
            Id = groups.Id,
            UseGrease = false,
            Groups = [.. groups.Groups.Where(group => !isFirefox || !IsFiniteField(group))],
        },

        KeyShareTlsExtension shares => new KeyShareTlsExtension { Id = shares.Id, Entries = shares.Entries, UseGrease = false },

        _ => extension,
    };

    /// <summary>Определяет, относится ли набор шифров к TLS 1.3.</summary>
    /// <param name="suite">Набор шифров.</param>
    /// <returns><see langword="true"/> для наборов TLS 1.3.</returns>
    private static bool IsTls13Suite(CipherSuite suite) => (ushort)suite is >= 0x1301 and <= 0x1305;

    /// <summary>Определяет, является ли группа конечнополевой.</summary>
    /// <param name="group">Группа обмена ключами.</param>
    /// <returns><see langword="true"/> для семейства FFDHE.</returns>
    private static bool IsFiniteField(NamedGroup group) => (ushort)group is >= 0x0100 and <= 0x0104;
}
