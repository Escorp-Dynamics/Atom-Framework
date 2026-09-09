using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Заголовки, которые запрещено передавать в HTTP/2 и HTTP/3.
/// </summary>
/// <remarks>
/// Правило общее для обеих версий и задано дословно: RFC 9113 §8.2.2 для HTTP/2 и RFC 9114 §4.2
/// для HTTP/3. Сообщение с такими заголовками считается МАЛФОРМИРОВАННЫМ, и получатель обязан
/// сбросить поток с кодом PROTOCOL_ERROR.
///
/// Ошибка эта коварна вдвойне. Во-первых, строгость серверов различается: Cloudflare такой запрос
/// обслуживает как ни в чём не бывало, а Google отвечает сбросом потока — то есть неполадка
/// выглядит сетевой и воспроизводится не везде. Во-вторых, попадают эти заголовки в блок не по
/// недосмотру: профиль браузера добавляет <c lang="text">connection: keep-alive</c> для HTTP/1.1, где он
/// уместен, и без фильтра он доезжает до кадра HEADERS.
///
/// Правило вынесено сюда, а не продублировано в каждой версии, намеренно: два одинаковых списка
/// неизбежно разойдутся, а разойдясь, дадут ту же ошибку ровно на одном из протоколов.
/// </remarks>
public static class ConnectionHeaderRules
{
    /// <summary>
    /// Определяет, запрещён ли заголовок в HTTP/2 и HTTP/3.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <returns><see langword="true"/>, если заголовок передавать нельзя.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsConnectionSpecific(string name)
        => string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Определяет, можно ли передать заголовок в блоке запроса.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <param name="value">Значение заголовка.</param>
    /// <returns><see langword="true"/>, если заголовок передавать нельзя.</returns>
    /// <remarks>
    /// Помимо списка соединения отсекаются ещё два случая. Устаревший <c lang="text">host</c> рядом с
    /// <c lang="text">:authority</c> — тоже некорректное сообщение (RFC 9113 §8.3.1). И <c lang="text">te</c>: он
    /// единственное исключение из списка соединения, но допустим ровно с одним значением —
    /// <c lang="text">trailers</c>; любое другое снова делает запрос малформированным.
    /// </remarks>
    public static bool IsProhibited(string name, string value)
    {
        if (IsConnectionSpecific(name)) return true;
        if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase)) return true;

        return string.Equals(name, "te", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "trailers", StringComparison.OrdinalIgnoreCase);
    }
}
