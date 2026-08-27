using Atom.Net.Https.Connections;
using Atom.Net.Https.Http;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Профили HTTP/2 под конкретные браузеры.
/// </summary>
/// <remarks>
/// Уровень HTTP/2 наблюдаем сервером не меньше, чем TLS: по нему строят отдельный отпечаток
/// (широко известный как «Akamai fingerprint»), который складывается из четырёх наблюдаемых вещей —
/// СОСТАВА и ПОРЯДКА параметров SETTINGS, величины приращения окна соединения, наличия и формы
/// кадров PRIORITY и ПОРЯДКА псевдозаголовков в запросе.
///
/// Именно поэтому значения здесь заданы явно и с комментариями, а не выведены «по смыслу»:
/// правдоподобные, но неверные числа дают отпечаток, не совпадающий ни с одним браузером, — то есть
/// ровно тот результат, которого мы избегаем.
/// </remarks>
public static class Http2ProfileCatalog
{
    /// <summary>
    /// Приращение окна соединения, которое Chrome отправляет сразу после преамбулы.
    /// </summary>
    /// <remarks>
    /// Chrome доводит окно соединения до 16 МБ, отправляя WINDOW_UPDATE на эту величину поверх
    /// стандартных 65535 байт. Значение входит в отпечаток напрямую.
    /// </remarks>
    public const uint ChromeConnectionWindowIncrement = 15663105;

    /// <summary>
    /// Возвращает параметры HTTP/2 для современного Chrome и производных от него браузеров.
    /// </summary>
    /// <returns>Настройки HTTP/2.</returns>
    public static Http2Settings CreateChrome()
        => new()
        {
            // Проверено живьём: akamai = 1:65536;2:0;4:6291456;6:262144|15663105|0|m,a,s,p
            PseudoHeaderOrder = "masp",

            // Порядок параметров воспроизводит наблюдаемый у Chrome. Сервер видит именно
            // последовательность, поэтому перестановка сама по себе меняет отпечаток.
            SettingsOrder =
            [
                new ConnectionSettings { Id = (uint)Http2SettingId.HeaderTableSize, Value = 65536 },
                new ConnectionSettings { Id = (uint)Http2SettingId.EnablePush, Value = 0 },
                new ConnectionSettings { Id = (uint)Http2SettingId.InitialWindowSize, Value = 6291456 },
                new ConnectionSettings { Id = (uint)Http2SettingId.MaxHeaderListSize, Value = 262144 },
            ],

            HeaderTableSize = 65536,
            InitialStreamWindowSize = 6291456,
            InitialWindowSize = 65535,
            MaxFrameSize = 16384,
            MaxConcurrentStreams = 1000,

            // Chrome дробит cookie на отдельные заголовки: это видно в блоке HPACK и отличает его,
            // например, от Firefox.
            UseCookieCrumbling = true,
            UsePreserveHeaderOrder = true,
            UseOriginalHeaderCase = false,
            UsePriorityFrames = false,
        };

    /// <summary>
    /// Возвращает параметры HTTP/2 для Firefox.
    /// </summary>
    /// <returns>Настройки HTTP/2.</returns>
    /// <remarks>
    /// Firefox отличается и составом параметров, и тем, что строит дерево приоритетов: он открывает
    /// служебные потоки с идентификаторами 3, 5, 7, 9 и 11 и раздаёт им веса. Без этого дерева
    /// профиль Firefox узнаётся сразу.
    /// </remarks>
    /// <summary>
    /// Возвращает параметры HTTP/2 для Firefox.
    /// </summary>
    /// <remarks>
    /// Сняты с настоящего Firefox 154 на локальном сервере TLS: SETTINGS в порядке
    /// HeaderTableSize=65536, EnablePush=0, InitialWindowSize=131072, MaxFrameSize=16384;
    /// приращение окна соединения 12517377; кадров PRIORITY НЕТ.
    ///
    /// Дерево приоритетов из потоков 3–11, которое Firefox строил годами, современная версия
    /// больше не отправляет — вместе с ним ушла и необходимость начинать нумерацию запросов
    /// с пятнадцатого потока.
    /// </remarks>
    public static Http2Settings CreateFirefox()
        => new()
        {
            // Замер настоящего Firefox 154: akamai = 1:65536;2:0;4:131072;5:16384|12517377|0|m,p,a,s
            PseudoHeaderOrder = "mpas",

            SettingsOrder =
            [
                new ConnectionSettings { Id = (uint)Http2SettingId.HeaderTableSize, Value = 65536 },
                new ConnectionSettings { Id = (uint)Http2SettingId.EnablePush, Value = 0 },
                new ConnectionSettings { Id = (uint)Http2SettingId.InitialWindowSize, Value = 131072 },
                new ConnectionSettings { Id = (uint)Http2SettingId.MaxFrameSize, Value = 16384 },
            ],

            HeaderTableSize = 65536,
            InitialStreamWindowSize = 131072,
            InitialWindowSize = 131072,
            MaxFrameSize = 16384,

            UseCookieCrumbling = false,
            UsePreserveHeaderOrder = true,
            UseOriginalHeaderCase = false,
            UsePriorityFrames = false,
            InitialStreamId = 1,
            PriorityTree = [],
            ConnectionWindowIncrement = 12_517_377,
        };

    /// <summary>
    /// Возвращает параметры HTTP/2 для Safari.
    /// </summary>
    /// <returns>Настройки HTTP/2.</returns>
    public static Http2Settings CreateSafari()
        => new()
        {
            // Слепок настоящего Safari 18.3:
            //   2:0;3:100;4:2097152;8:1;9:1|10420225|0|m,s,a,p
            //
            // Отличий от движков Chromium и Gecko здесь много, и каждое наблюдаемо. Размера
            // таблицы заголовков Safari не объявляет ВООБЩЕ — а объявить его значит сказать о
            // себе то, чего этот браузер не говорит. Зато он единственный объявляет расширенный
            // CONNECT (8) и отказ от приоритетов RFC 7540 (9). Приращение окна тоже своё.
            PseudoHeaderOrder = "msap",

            SettingsOrder =
            [
                new ConnectionSettings { Id = (uint)Http2SettingId.EnablePush, Value = 0 },
                new ConnectionSettings { Id = (uint)Http2SettingId.MaxConcurrentStreams, Value = 100 },
                new ConnectionSettings { Id = (uint)Http2SettingId.InitialWindowSize, Value = 2097152 },
                new ConnectionSettings { Id = (uint)Http2SettingId.EnableConnectProtocol, Value = 1 },
                new ConnectionSettings { Id = (uint)Http2SettingId.NoRfc7540Priorities, Value = 1 },
            ],

            HeaderTableSize = 4096,
            InitialStreamWindowSize = 2097152,
            InitialWindowSize = 65535,
            MaxFrameSize = 16384,
            MaxConcurrentStreams = 100,
            ConnectionWindowIncrement = 10_420_225,

            UseCookieCrumbling = false,
            UsePreserveHeaderOrder = true,
            UseOriginalHeaderCase = false,
            UsePriorityFrames = false,
        };
}
