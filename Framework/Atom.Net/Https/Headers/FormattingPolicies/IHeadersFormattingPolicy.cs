namespace Atom.Net.Https.Headers;

/// <summary>
/// Политика форматирования заголовков: порядок, регистр имён, разделение Cookie, псевдозаголовки.
/// </summary>
public interface IHeadersFormattingPolicy
{
    /// <summary>
    /// Определяет, должен ли форматировщик работать в мобильном режиме.
    /// </summary>
    bool IsMobile { get; init; }

    /// <summary>
    /// Задаёт порядок псевдозаголовков :method, :scheme, :authority, :path (формат - m, s, a, p).
    /// </summary>
    IReadOnlyDictionary<RequestKind, IEnumerable<char>> PseudoHeadersOrder { get; set; }

    /// <summary>
    /// Форматирует обычные заголовки: порядок, регистр имён, склейка/дробление Cookie.
    /// </summary>
    /// <param name="input">Входные заголовки.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="requestKind">Тип запроса.</param>
    /// <param name="useCookieCrumbling">Указывает, требуется ли использовать cookie crumbling.</param>
    IEnumerable<KeyValuePair<string, string>> Format(IDictionary<string, string> input, Version requestVersion, RequestKind requestKind, bool useCookieCrumbling);
}