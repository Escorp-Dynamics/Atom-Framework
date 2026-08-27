namespace Atom.Net.Https.Http3;

/// <summary>
/// Наборы параметров HTTP/3, наблюдаемые сервером в начале соединения.
/// </summary>
/// <remarks>
/// Роль та же, что у SETTINGS в HTTP/2: параметры уходят первым же кадром по управляющему потоку,
/// и по их составу и значениям сервер отличает клиентов друг от друга.
///
/// Прежде набор был ЗАШИТ в реализацию соединения, то есть любой профиль — включая Firefox и
/// мобильные — представлялся серверу как Chrome. Профильный слой это устраняет; каждый набор
/// честно помечен: снят он с браузера или взят по описанию.
/// </remarks>
public static class Http3ProfileCatalog
{
    /// <summary>
    /// Параметры Chrome.
    /// </summary>
    /// <returns>Набор параметров.</returns>
    /// <remarks>
    /// Снято с настоящего Chrome через его журнал сети (событие <c>HTTP3_SETTINGS_SENT</c>):
    /// таблица QPACK 65536, поле заголовков 262144, сто заблокированных потоков, датаграммы.
    ///
    /// ★ Ёмкость таблицы QPACK — не декоративное число: декодировщик обязан завести таблицу
    /// РОВНО такого же размера. Объявив серверу больше, чем держим сами, мы вытесняем записи,
    /// на которые он ссылается, и ответ перестаёт разбираться. Ровно это и происходило с
    /// <c>www.google.com</c>, пока ёмкость бралась из значения по умолчанию.
    /// </remarks>
    public static IReadOnlyList<(Http3SettingId Id, ulong Value)> CreateChrome() =>
    [
        (Http3SettingId.QpackMaxTableCapacity, 65536),
        (Http3SettingId.MaxFieldSectionSize, 262144),
        (Http3SettingId.QpackBlockedStreams, 100),
        (Http3SettingId.H3Datagram, 1),
    ];

    /// <summary>
    /// Параметры Firefox.
    /// </summary>
    /// <returns>Набор параметров.</returns>
    /// <remarks>
    /// ★ Состав и порядок взяты из ИСХОДНИКОВ стека, а не с провода: журнал neqo в файл не
    /// пишется, а сами параметры уходят под ключами и пассивно не наблюдаемы.
    ///
    /// Источник — <c>mozilla/neqo</c>, преобразование <c>Http3Parameters</c> в <c>HSettings</c>
    /// (<c>neqo-http3/src/settings.rs</c>), сверено с веткой <c>release</c> самого Firefox
    /// (154.0.1, вендоренный neqo 0.29.0). Порядок жёстко задан вектором —
    /// <c>MaxTableCapacity</c>, <c>BlockedStreams</c>, <c>EnableWebTransport</c>,
    /// <c>EnableH3Datagram</c>, <c>EnableConnect</c>. Значения задаёт сам Firefox в
    /// <c>netwerk/socket/neqo_glue</c>: <c>.connect(true)</c> и <c>.http3_datagram(true)</c>
    /// стоят там жёстко, а размер таблицы и число заблокированных потоков приходят из настроек
    /// <c>network.http.http3.default-qpack-table-size</c> = 65536 и
    /// <c>default-max-stream-blocked</c> = 20.
    ///
    /// ★ Главное отличие от движков Chromium: предела списка заголовков (0x06) Firefox НЕ
    /// отправляет вовсе, зато отправляет три флага, которых нет у них. Прежний наш набор был
    /// догадкой и содержал ровно наоборот — предел был, флагов не было.
    ///
    /// Две тонкости, каждая из которых легко выходит неверной по здравому смыслу:
    ///
    /// Флаг WebTransport отправляется со значением НОЛЬ на обычных соединениях. Единица там
    /// появляется, только когда соединение заведено под WebTransport; сам параметр в наборе
    /// присутствует всегда.
    ///
    /// Датаграммы объявляются ДВАЖДЫ — черновым номером и окончательным, подряд. Это не описка
    /// реализации, а её поведение: при включённых датаграммах записываются оба.
    ///
    /// Предела списка заголовков (0x06) в наборе НЕТ вовсе, хотя движки Chromium его шлют.
    /// </remarks>
    public static IReadOnlyList<(Http3SettingId Id, ulong Value)> CreateFirefox() =>
    [
        (Http3SettingId.QpackMaxTableCapacity, 65536),
        (Http3SettingId.QpackBlockedStreams, 20),
        (Http3SettingId.EnableWebTransport, 0),
        (Http3SettingId.H3DatagramDraft04, 1),
        (Http3SettingId.H3Datagram, 1),
        (Http3SettingId.EnableConnectProtocol, 1),
    ];
}
