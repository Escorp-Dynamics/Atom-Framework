using System.Runtime.CompilerServices;
using Atom.Net.Https.Http;

using Atom.Net.Https.Headers;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Сборка списка заголовков запроса HTTP/2 в порядке, характерном для браузера.
/// </summary>
/// <remarks>
/// Порядок псевдозаголовков — наблюдаемая часть отпечатка. Браузеры на движке Chromium
/// отправляют их в порядке <c>:method</c>, <c>:authority</c>, <c>:scheme</c>, <c>:path</c>, тогда
/// как многие библиотеки используют «естественный» порядок из спецификации
/// (<c>:method</c>, <c>:scheme</c>, <c>:authority</c>, <c>:path</c>). Различие в одной перестановке
/// достаточно, чтобы клиент перестал выглядеть браузером.
///
/// Второй наблюдаемый признак — обращение с cookie. Chromium дробит их на отдельные заголовки по
/// одному значению в каждом, что заметно в блоке HPACK; Firefox и Safari отправляют одной строкой.
/// </remarks>
public static class Http2RequestHeaders
{
    /// <summary>
    /// Строит упорядоченный список заголовков для кадра HEADERS.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="authority">Значение <c>:authority</c> — хост с портом, если он нестандартный.</param>
    /// <param name="path">Путь с query-строкой.</param>
    /// <param name="headers">Обычные заголовки запроса в порядке отправки.</param>
    /// <param name="settings">Профиль HTTP/2.</param>
    /// <param name="scheme">Схема запроса.</param>
    /// <returns>Список пар для кодирования HPACK в порядке отправки.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> Build(
        string method,
        string authority,
        string path,
        IEnumerable<KeyValuePair<string, string>> headers,
        in Http2Settings settings,
        string scheme = "https")
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(authority);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(headers);

        var result = new List<KeyValuePair<string, string>>(capacity: 16);

        // Порядок псевдозаголовков задаёт профиль: он наблюдаем и у браузеров различается.
        foreach (var pseudo in settings.PseudoHeaderOrder)
        {
            switch (pseudo)
            {
                case 'm': result.Add(new KeyValuePair<string, string>(":method", method)); break;
                case 'a': result.Add(new KeyValuePair<string, string>(":authority", authority)); break;
                case 's': result.Add(new KeyValuePair<string, string>(":scheme", scheme)); break;
                case 'p': result.Add(new KeyValuePair<string, string>(":path", path)); break;
                default: throw new InvalidOperationException($"Неизвестный псевдозаголовок '{pseudo}' в порядке профиля");
            }
        }

        if (result.Count is not 4) throw new InvalidOperationException("Порядок псевдозаголовков обязан называть все четыре");

        var crumbleCookies = settings.UseCookieCrumbling;

        foreach (var header in headers)
        {
            if (string.IsNullOrEmpty(header.Key)) continue;

            // Имена заголовков в HTTP/2 обязаны быть в нижнем регистре: заглавные буквы делают
            // сообщение некорректным, а не просто нестандартным.
            var name = settings.UseOriginalHeaderCase ? header.Key : header.Key.ToLowerInvariant();

            // Запрещённые в HTTP/2 заголовки отсекаются общим правилом — тем же, что и в HTTP/3.
            if (ConnectionHeaderRules.IsProhibited(name, header.Value)) continue;

            if (crumbleCookies && string.Equals(name, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                AppendCrumbledCookies(result, header.Value);
                continue;
            }

            result.Add(new KeyValuePair<string, string>(name, header.Value));
        }

        return result;
    }

    /// <summary>
    /// Разбивает строку cookie на отдельные заголовки — по одной паре в каждом.
    /// </summary>
    /// <param name="destination">Список, в который добавляются заголовки.</param>
    /// <param name="value">Исходное значение заголовка cookie.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendCrumbledCookies(List<KeyValuePair<string, string>> destination, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        var rest = value.AsSpan();

        while (!rest.IsEmpty)
        {
            var separator = rest.IndexOf(';');
            var piece = separator < 0 ? rest : rest[..separator];
            rest = separator < 0 ? [] : rest[(separator + 1)..];

            piece = piece.Trim();
            if (!piece.IsEmpty) destination.Add(new KeyValuePair<string, string>("cookie", piece.ToString()));
        }
    }
}
