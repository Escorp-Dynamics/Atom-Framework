namespace Atom.Net.Https.Headers;

/// <summary>
/// Читает заголовки запроса ровно в том виде, в каком они были заданы.
/// </summary>
/// <remarks>
/// ★ Существует потому, что обычное перечисление <see cref="System.Net.Http.Headers.HttpHeaders"/>
/// отдаёт РАЗОБРАННЫЕ значения, а не исходную строку. Для типизированных заголовков платформа
/// разбивает значение на элементы, и обход даёт их по отдельности: строка агента распадается на
/// пять кусков по пробелам, <c lang="text">accept</c> — на восемь по запятым, <c lang="text">accept-encoding</c> — на
/// четыре. Каждый кусок уезжал на провод ОТДЕЛЬНЫМ заголовком.
///
/// Наблюдать это ничем, кроме зеркала, нельзя: соединение работает, сигнатура HTTP/2 не
/// затрагивается — она про SETTINGS и псевдозаголовки, — а сервер видит запрос, какого браузеры
/// не отправляют. Замер на tls.peet.ws показывал у нас двадцать шесть строк заголовков там, где
/// настоящий Chrome отправляет тринадцать.
///
/// Лечится обращением к нерасчленённому представлению: оно возвращает значение таким, каким его
/// записали, без разбора и без домыслов о разделителе. Прежний путь для HTTP/1.1 склеивал куски
/// обратно вручную, разбирая каждый типизированный заголовок особым случаем, — то есть повторял
/// работу платформы и расходился с ней на всём, чего не предусмотрел.
/// </remarks>
public static class RequestHeaderReader
{
    /// <summary>
    /// Собирает заголовки запроса вместе с заголовками содержимого.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="destination">Список, в который добавляются пары.</param>
    /// <param name="lowercaseNames">Приводить ли имена к нижнему регистру (требование HTTP/2 и HTTP/3).</param>
    /// <param name="skip">Необязательная проверка: заголовки, для которых она вернёт истину, пропускаются.</param>
    public static void Collect(
        HttpRequestMessage request,
        ICollection<KeyValuePair<string, string>> destination,
        bool lowercaseNames,
        Func<string, string, bool>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);

        Append(request.Headers.NonValidated, destination, lowercaseNames, skip);

        if (request.Content is not null) Append(request.Content.Headers.NonValidated, destination, lowercaseNames, skip);
    }

    /// <summary>
    /// Возвращает значение заголовка в исходном виде.
    /// </summary>
    /// <param name="headers">Заголовки.</param>
    /// <param name="name">Имя заголовка.</param>
    /// <returns>Значение или <see langword="null"/>, если заголовка нет.</returns>
    public static string? GetValue(System.Net.Http.Headers.HttpHeaders headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return headers.NonValidated.TryGetValues(name, out var values) ? Join(values) : null;
    }

    private static void Append(
        System.Net.Http.Headers.HttpHeadersNonValidated headers,
        ICollection<KeyValuePair<string, string>> destination,
        bool lowercaseNames,
        Func<string, string, bool>? skip)
    {
        foreach (var header in headers)
        {
            var name = lowercaseNames ? header.Key.ToLowerInvariant() : header.Key;
            var value = Join(header.Value);

            if (skip is not null && skip(name, value)) continue;

            destination.Add(new KeyValuePair<string, string>(name, value));
        }
    }

    /// <summary>
    /// Склеивает значения одного заголовка.
    /// </summary>
    /// <param name="values">Значения.</param>
    /// <returns>Готовая строка.</returns>
    /// <remarks>
    /// В подавляющем большинстве случаев значение ровно одно и возвращается как есть. Несколько
    /// значений появляются, только если заголовок задавали несколько раз, — и тогда правило
    /// склейки задано HTTP: запятая с пробелом.
    /// </remarks>
    private static string Join(System.Net.Http.Headers.HeaderStringValues values)
        => values.Count switch
        {
            0 => string.Empty,

            // Одно значение — самый частый случай: возвращаем его как есть, без склейки.
            1 => values.ToString(),
            _ => string.Join(", ", values),
        };
}
