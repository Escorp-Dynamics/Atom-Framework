using System.Net;
using System.Runtime.InteropServices;
using Atom.Net.Https.Http;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Единый immutable snapshot браузерного профиля для orchestration transport и header слоёв.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BrowserProfile
{
    public BrowserProfile() { }

    /// <summary>
    /// Человекочитаемое имя профиля.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Строка User-Agent для этого профиля.
    /// </summary>
    public required string UserAgent { get; init; }

    /// <summary>
    /// Предпочитаемая версия HTTP для стартового запроса.
    /// </summary>
    public Version PreferredHttpVersion { get; init; } = HttpVersion.Version11;

    /// <summary>
    /// Политика согласования версии HTTP.
    /// </summary>
    public HttpVersionPolicy VersionPolicy { get; init; } = HttpVersionPolicy.RequestVersionOrLower;

    /// <summary>
    /// Настройки TCP-поведения профиля.
    /// </summary>
    public TcpSettings Tcp { get; init; }

    /// <summary>
    /// Настройки TLS-поведения профиля.
    /// </summary>
    public TlsSettings Tls { get; init; }

    /// <summary>
    /// Минимальный snapshot browser-shaped header поведения.
    /// </summary>
    public BrowserHeaderProfile Headers { get; init; }

    /// <summary>
    /// Работает ли профиль на мобильном устройстве.
    /// </summary>
    /// <remarks>
    /// Влияет на наблюдаемую часть запроса: подсказка <c lang="text">sec-ch-ua-mobile</c> и платформа в
    /// подсказках клиента. Транспортный отпечаток при этом остаётся отпечатком ДВИЖКА: Chrome на
    /// Android говорит тем же BoringSSL, что и на настольной системе, а Safari и Chrome на iOS —
    /// одним и тем же WebKit, потому что иного движка на этой платформе нет.
    /// </remarks>
    public bool IsMobile { get; init; }

    /// <summary>
    /// Платформа, объявляемая в подсказках клиента; пусто — вывести из строки агента.
    /// </summary>
    public string? ClientHintsPlatform { get; init; }

    /// <summary>
    /// Настройки HTTP/2 профиля: состав и порядок SETTINGS, приращение окна, кадры приоритета.
    /// </summary>
    /// <remarks>
    /// Часть профиля, а не отдельная настройка соединения: то, что сервер видит сразу после
    /// преамбулы, отличает браузеры между собой не хуже набора шифров. Chrome и Firefox
    /// расходятся и порядком настроек, и наличием дерева приоритетов — описывать это где-то,
    /// кроме профиля, значило бы позволить транспорту и заголовкам разъехаться.
    ///
    /// Значение <see langword="null"/> означает, что профиль не описывает HTTP/2; соединение
    /// возьмёт настройки по умолчанию.
    /// </remarks>
    public Http2Settings? Http2 { get; init; }

    /// <summary>
    /// Параметры HTTP/3, наблюдаемые сервером в начале соединения.
    /// </summary>
    /// <remarks>
    /// Роль та же, что у <see cref="Http2"/>: набор уходит первым кадром по управляющему потоку и
    /// входит в наблюдаемый облик клиента. Пусто означает набор Chrome — так было и раньше, когда
    /// он был просто зашит в реализацию соединения.
    /// </remarks>
    public IReadOnlyList<(Http3.Http3SettingId Id, ulong Value)>? Http3 { get; init; }

    /// <summary>
    /// Параметры транспорта QUIC, наблюдаемые сервером внутри ClientHello.
    /// </summary>
    /// <remarks>
    /// У браузеров они различаются целиком — величинами, составом и порядком, — и относятся к
    /// отпечатку так же, как расширения TLS. Пусто означает набор движков Chromium.
    /// </remarks>
    public Quic.QuicTransportParameters? QuicTransport { get; init; }
}