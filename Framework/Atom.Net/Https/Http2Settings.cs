using System.Runtime.CompilerServices;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Headers.HPack;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Https.Http;

/// <summary>
/// Представляет настройки HTTP/2.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Http2Settings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct Http2Settings() : IEquatable<Http2Settings>
{
    /// <summary>
    /// Политика форматирования заголовков.
    /// </summary>
    public IHeadersFormattingPolicy? HeadersFormattingPolicy { get; init; }

    /// <summary>
    /// SETTINGS в ТОМ ЖЕ порядке, что у браузера.
    /// </summary>
    public IEnumerable<ConnectionSettings> SettingsOrder { get; init; } = [];

    /// <summary>
    /// Определяет начальный размер окна получения потока HTTP2 для всех подключений.
    /// </summary>
    public uint InitialStreamWindowSize { get; init; } = 65535;

    /// <summary>
    /// Начальный размер окна потока.
    /// </summary>
    public uint InitialWindowSize { get; init; } = 65535;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxConcurrentStreams { get; init; }

    /// <summary>
    /// Размер динамической таблицы HPACK.
    /// </summary>
    public uint HeaderTableSize { get; init; } = 65535;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxFrameSize { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool UseConnectProtocol { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool UsePriorityFrames { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public IEnumerable<StreamPriority> PriorityTree { get; init; } = [];

    /// <summary>
    /// Номер первого потока, открываемого под запрос.
    /// </summary>
    /// <remarks>
    /// Обязан быть нечётным и БОЛЬШЕ любого идентификатора, упомянутого в дереве приоритетов:
    /// спецификация требует, чтобы номера открываемых клиентом потоков только возрастали, и
    /// запрос на потоке, который преамбула уже назвала, сервер просто не обслуживает — соединение
    /// зависает без единой ошибки. Firefox шлёт дерево из потоков 3–11 и начинает запросы с 15;
    /// Chrome и Safari дерева не строят и начинают с единицы.
    /// </remarks>
    public int InitialStreamId { get; init; } = 1;

    /// <summary>
    /// Приращение окна соединения, отправляемое сразу после преамбулы.
    /// </summary>
    /// <remarks>
    /// Часть отпечатка: значение у браузеров различается и наблюдаемо в первой же датаграмме.
    /// Замерено: Chromium 15663105, Firefox 12517377.
    /// </remarks>
    public uint ConnectionWindowIncrement { get; init; } = 15_663_105;

    /// <summary>
    /// Порядок псевдозаголовков в блоке HEADERS.
    /// </summary>
    /// <remarks>
    /// ★ Наблюдаем напрямую: он входит последним полем в сигнатуру HTTP/2, которую строят по
    /// началу соединения. У браузеров он РАЗНЫЙ и устойчивый — Chrome отправляет
    /// <c lang="text">m,a,s,p</c>, Firefox <c lang="text">m,p,a,s</c>. Раньше порядок был зашит хромиумовским для всех, и
    /// профиль Firefox совпадал с браузером во всём, кроме этого поля.
    ///
    /// Буквы — первые буквы имён: m = :method, a = :authority, s = :scheme, p = :path. Так их
    /// записывают и в самой сигнатуре, поэтому сверять значение с замером можно глазами.
    /// </remarks>
    public string PseudoHeaderOrder { get; init; } = "masp";

    /// <summary>
    /// 
    /// </summary>
    public bool UseCookieCrumbling { get; init; } = true;

    /// <summary>
    /// 
    /// </summary>
    public bool UsePreserveHeaderOrder { get; init; } = true;

    /// <summary>
    /// 
    /// </summary>
    public bool UseOriginalHeaderCase { get; init; } = true;

    /// <summary>
    /// 
    /// </summary>
    public TimeSpan PrefaceDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 
    /// </summary>
    public TimeSpan SettingsAckDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 
    /// </summary>
    public bool UsePadHeaders { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool UsePadData { get; init; }

    /// <summary>
    /// Возвращает или задает значение, указывающее, можно ли установить дополнительные подключения HTTP/2 к тому же серверу.
    /// </summary>
    public bool UseMultipleConnections { get; init; }

    /// <summary>
    /// Добавлять зарезервированные ключи.
    /// </summary>
    public bool UseGreaseOnSettings { get; init; }

    /// <summary>
    /// SETTINGS_MAX_HEADER_LIST_SIZE.
    /// </summary>
    public uint MaxHeaderListSize { get; init; }

    /// <summary>
    /// Настройки TLS.
    /// </summary>
    public TlsSettings Tls { get; init; }

    /// <summary>
    /// Настройки TCP.
    /// </summary>
    public TcpSettings Tcp { get; init; }

    /// <summary>
    /// Настройки HPack.
    /// </summary>
    public HPackSettings HPack { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(HeadersFormattingPolicy?.GetHashCode());
        hash.Add(InitialStreamWindowSize.GetHashCode());
        hash.Add(InitialWindowSize.GetHashCode());
        hash.Add(MaxConcurrentStreams.GetHashCode());
        hash.Add(HeaderTableSize.GetHashCode());
        hash.Add(MaxFrameSize.GetHashCode());
        hash.Add(UseConnectProtocol.GetHashCode());
        hash.Add(UsePriorityFrames.GetHashCode());
        hash.Add(PriorityTree.GetHashCode());
        hash.Add(UseCookieCrumbling.GetHashCode());
        hash.Add(UsePreserveHeaderOrder.GetHashCode());
        hash.Add(UseOriginalHeaderCase.GetHashCode());
        hash.Add(PrefaceDelay.GetHashCode());
        hash.Add(SettingsAckDelay.GetHashCode());
        hash.Add(UsePadHeaders.GetHashCode());
        hash.Add(UsePadData.GetHashCode());
        hash.Add(UseMultipleConnections.GetHashCode());
        hash.Add(UseGreaseOnSettings.GetHashCode());
        hash.Add(MaxHeaderListSize.GetHashCode());
        hash.Add(Tls.GetHashCode());
        hash.Add(Tcp.GetHashCode());
        hash.Add(HPack.GetHashCode());

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Http2Settings other) => ((HeadersFormattingPolicy is null && other.HeadersFormattingPolicy is null) || (HeadersFormattingPolicy is not null && other.HeadersFormattingPolicy is not null && HeadersFormattingPolicy.Equals(other.HeadersFormattingPolicy))) && InitialStreamWindowSize.Equals(other.InitialStreamWindowSize) && InitialWindowSize.Equals(other.InitialWindowSize)
        && MaxConcurrentStreams.Equals(other.MaxConcurrentStreams) && HeaderTableSize.Equals(other.HeaderTableSize)
        && MaxFrameSize.Equals(other.MaxFrameSize) && UseConnectProtocol.Equals(other.UseConnectProtocol)
        && UsePriorityFrames.Equals(other.UsePriorityFrames) && PriorityTree.Equals(other.PriorityTree)
        && UseCookieCrumbling.Equals(other.UseCookieCrumbling) && UsePreserveHeaderOrder.Equals(other.UsePreserveHeaderOrder)
        && UseOriginalHeaderCase.Equals(other.UseOriginalHeaderCase) && PrefaceDelay.Equals(other.PrefaceDelay)
        && SettingsAckDelay.Equals(other.SettingsAckDelay) && UsePadHeaders.Equals(other.UsePadHeaders)
        && UsePadData.Equals(other.UsePadData) && UseMultipleConnections.Equals(other.UseMultipleConnections)
        && UseGreaseOnSettings.Equals(other.UseGreaseOnSettings) && MaxHeaderListSize.Equals(other.MaxHeaderListSize)
        && Tls.Equals(other.Tls) && Tcp.Equals(other.Tcp) && HPack.Equals(other.HPack);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        Http2Settings other => Equals(other),
        _ => default,
    };

    /// <summary>
    /// Определяет, равны ли два набора настроек HTTP/2.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если значения совпадают.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Http2Settings left, Http2Settings right) => left.Equals(right);

    /// <summary>
    /// Определяет, различаются ли два набора настроек HTTP/2.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если значения различаются.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Http2Settings left, Http2Settings right) => !(left == right);
}