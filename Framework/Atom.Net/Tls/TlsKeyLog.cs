using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Atom.Net.Tls;

/// <summary>
/// Журнал секретов TLS в формате NSS key log — том самом, который читает Wireshark.
/// </summary>
/// <remarks>
/// ★ Без этого журнала собственный трафик наблюдать НЕЧЕМ. Дамп соединения показывает только
/// зашифрованные записи, а когда сервер отвечает не тем, чего мы ждали, вопрос ровно один: что
/// именно пришло на провод. Внутренняя трассировка на него не отвечает — она показывает то, что
/// разобрал наш же код, то есть ту сторону, которая и подозревается в ошибке.
///
/// Формат придуман NSS и принят всеми: <c lang="text">curl</c>, Chrome и Firefox пишут его по той же
/// переменной окружения <c lang="text">SSLKEYLOGFILE</c>. Строка имеет вид
/// <c lang="text">МЕТКА client_random_hex secret_hex</c>; Wireshark связывает сессию с секретом именно по
/// <c lang="text">client_random</c>, поэтому запись без него бесполезна.
///
/// ★ Журнал выключен, пока переменная не задана, и это не перестраховка: его содержимое позволяет
/// расшифровать записанный трафик кому угодно. Поэтому здесь нет ни единой строки, печатающей
/// путь или секреты куда-либо ещё, — цена случайной утечки в общий журнал приложения слишком
/// велика.
///
/// Сбой записи НИКОГДА не выходит наружу: диагностика не имеет права обрывать соединение,
/// которое она пришла наблюдать.
/// </remarks>
public static class TlsKeyLog
{
    /// <summary>Переменная окружения, задающая путь к журналу; де-факто стандарт NSS.</summary>
    public const string EnvironmentVariableName = "SSLKEYLOGFILE";

    /// <summary>Метка раннего секрета трафика клиента (0-RTT, TLS 1.3).</summary>
    public const string ClientEarlyTrafficSecretLabel = "CLIENT_EARLY_TRAFFIC_SECRET";

    /// <summary>Метка секрета трафика рукопожатия клиента (TLS 1.3).</summary>
    public const string ClientHandshakeTrafficSecretLabel = "CLIENT_HANDSHAKE_TRAFFIC_SECRET";

    /// <summary>Метка секрета трафика рукопожатия сервера (TLS 1.3).</summary>
    public const string ServerHandshakeTrafficSecretLabel = "SERVER_HANDSHAKE_TRAFFIC_SECRET";

    /// <summary>Метка первого прикладного секрета трафика клиента (TLS 1.3).</summary>
    public const string ClientTrafficSecretLabel = "CLIENT_TRAFFIC_SECRET_0";

    /// <summary>Метка первого прикладного секрета трафика сервера (TLS 1.3).</summary>
    public const string ServerTrafficSecretLabel = "SERVER_TRAFFIC_SECRET_0";

    /// <summary>Метка master secret для TLS 1.2 и ниже.</summary>
    public const string ClientRandomLabel = "CLIENT_RANDOM";

    /// <summary>Длина случайного числа ClientHello: спецификация фиксирует её навсегда.</summary>
    private const int ClientRandomLength = 32;

    /// <summary>Сериализует запись: файл один на процесс, а рукопожатий одновременно много.</summary>
    /// <remarks>
    /// Строки короткие, но без общей блокировки параллельные соединения перемешали бы их внутри
    /// одной строки, и журнал стал бы нечитаемым ровно в тот момент, когда он нужен, — под
    /// нагрузкой.
    /// </remarks>
    private static readonly Lock Gate = new();

    private static string? explicitFilePath;
    private static FileStream? sink;
    private static string? sinkPath;
    private static bool disabled;

    /// <summary>
    /// Действующий путь к журналу: явная настройка, иначе переменная окружения; <see langword="null"/> — журнал выключен.
    /// </summary>
    /// <remarks>
    /// Присваивание включает журнал программно, не трогая окружение процесса, а <see langword="null"/>
    /// возвращает управление переменной <see cref="EnvironmentVariableName"/>.
    ///
    /// ★ Настройка намеренно живёт ЗДЕСЬ, а не в <see cref="TlsSettings"/>: тот профиль описывает
    /// отпечаток соединения и участвует в сравнении профилей каталога. Путь к файлу отладки к
    /// отпечатку отношения не имеет, и два одинаковых профиля перестали бы быть равными из-за
    /// него.
    /// </remarks>
    public static string? FilePath
    {
        get => Normalize(explicitFilePath) ?? Normalize(Environment.GetEnvironmentVariable(EnvironmentVariableName));
        set => explicitFilePath = value;
    }

    /// <summary>Пишется ли журнал прямо сейчас.</summary>
    public static bool IsEnabled => !disabled && FilePath is not null;

    /// <summary>
    /// Дописывает в журнал одну строку с секретом.
    /// </summary>
    /// <param name="label">Метка секрета, например <see cref="ClientTrafficSecretLabel"/>.</param>
    /// <param name="clientRandom">Случайное число из ClientHello, ровно <c lang="text">32</c> байта.</param>
    /// <param name="secret">Сам секрет; пустой пропускается.</param>
    /// <remarks>
    /// Метод не бросает исключений вообще. Неполные данные (рукопожатие ещё не дошло до
    /// ClientHello, секрет не выведен) молча пропускаются: место вызова обязано оставаться
    /// простой строчкой без проверок вокруг.
    /// </remarks>
    public static void Write(string label, ReadOnlySpan<byte> clientRandom, ReadOnlySpan<byte> secret)
    {
        // Порядок проверок не случаен: пока журнал выключен — а это обычный режим работы, — ни
        // шестнадцатеричного преобразования секрета, ни выделения строки не происходит.
        if (disabled) return;

        var target = FilePath;
        if (target is null) return;

        if (!TryFormatLine(label, clientRandom, secret, out var line)) return;

        lock (Gate)
        {
            try
            {
                Append(target, line);
            }
            catch
            {
                // Единственная задача этого catch — не дать диагностике убить соединение.
                CloseSink();
            }
        }
    }

    /// <summary>
    /// Закрывает файл журнала и снимает отметку об ошибке открытия.
    /// </summary>
    /// <remarks>
    /// Каждая строка и так сбрасывается на диск сразу, поэтому метод нужен не ради сохранности
    /// данных, а там, где файл требуется отдать другому процессу, удалить или сменить путь.
    /// Следующая запись откроет журнал заново.
    /// </remarks>
    public static void Close()
    {
        lock (Gate)
        {
            CloseSink();
            disabled = false;
        }
    }

    /// <summary>
    /// Собирает строку журнала в формате NSS без завершающего перевода строки.
    /// </summary>
    /// <param name="label">Метка секрета.</param>
    /// <param name="clientRandom">Случайное число ClientHello, ровно <c lang="text">32</c> байта.</param>
    /// <param name="secret">Секрет.</param>
    /// <param name="line">Готовая строка вида <c lang="text">МЕТКА client_random_hex secret_hex</c>.</param>
    /// <returns><see langword="true"/>, если строку удалось собрать.</returns>
    /// <remarks>
    /// ★ Длина <paramref name="clientRandom"/> проверяется жёстко: Wireshark ищет сессию по этому
    /// полю, и запись с обрезанным значением не отказывает, а тихо не находит ничего — отладка
    /// уходит в поиск несуществующей ошибки в самом TLS.
    ///
    /// Регистр шестнадцатеричных цифр парсеру безразличен; берём нижний, как <c lang="text">curl</c> и NSS.
    /// </remarks>
    public static bool TryFormatLine(string label, ReadOnlySpan<byte> clientRandom, ReadOnlySpan<byte> secret, [NotNullWhen(true)] out string? line)
    {
        line = default;

        if (string.IsNullOrWhiteSpace(label)) return false;
        if (clientRandom.Length != ClientRandomLength) return false;
        if (secret.IsEmpty) return false;

        line = string.Concat(label, " ", Convert.ToHexStringLower(clientRandom), " ", Convert.ToHexStringLower(secret));

        return true;
    }

    /// <summary>
    /// Приводит заданный путь к «включено/выключено».
    /// </summary>
    /// <param name="value">Значение настройки или переменной окружения.</param>
    /// <returns>Путь либо <see langword="null"/>, если журнал писать некуда.</returns>
    /// <remarks>
    /// Пустая переменная — это привычный способ ВЫКЛЮЧИТЬ настройку в оболочке; считать её
    /// включением значило бы попытаться открыть файл с пустым именем на каждом рукопожатии.
    /// </remarks>
    internal static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? default : value;

    /// <summary>
    /// Дописывает строку в файл, открывая его при необходимости.
    /// </summary>
    /// <remarks>
    /// Файл открывается ОДИН раз и держится открытым: на рукопожатие приходится до пяти строк, и
    /// открывать файл на каждую значило бы добавить к соединению пять системных вызовов открытия
    /// там, где хватает одного на весь процесс. <see cref="FileShare.ReadWrite"/> оставляет файл
    /// доступным Wireshark и соседним процессам, пишущим в тот же журнал, а
    /// <see cref="FileMode.Append"/> гарантирует, что прежние сессии не будут затёрты.
    /// </remarks>
    private static void Append(string target, string line)
    {
        if (sink is null || !string.Equals(sinkPath, target, StringComparison.Ordinal))
        {
            CloseSink();

            try
            {
                sink = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                sinkPath = target;
            }
            catch
            {
                // Открыть не удалось — это ошибка настройки (нет каталога, нет прав), и повторять
                // её на каждом рукопожатии бессмысленно: журнал выключается до конца процесса.
                disabled = true;
                throw;
            }
        }

        // Строка журнала короткая по построению (метка, 64 знака client_random и до 96 знаков
        // секрета), поэтому запас на стеке покрывает её целиком; ветка с массивом оставлена на
        // случай неизвестной сегодня метки.
        Span<byte> buffer = stackalloc byte[256];
        var length = line.Length + 1;
        Span<byte> bytes = length <= buffer.Length ? buffer[..length] : new byte[length];

        Encoding.ASCII.GetBytes(line, bytes);

        // Перевод строки именно \n: формат NSS построчный, и лишний \r попадает в значение секрета
        // у части разборщиков.
        bytes[^1] = (byte)'\n';

        sink.Write(bytes);
        sink.Flush();
    }

    private static void CloseSink()
    {
        try
        {
            sink?.Dispose();
        }
        catch
        {
            // Поток уже мог быть разрушен — состояние всё равно сбрасывается ниже.
        }

        sink = default;
        sinkPath = default;
    }
}
