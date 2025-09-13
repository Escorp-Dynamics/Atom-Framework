namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет базовый интерфейс для реализации декодировщиков заголовков.
/// </summary>
public interface IHeadersDecoder
{
    /// <summary>
    /// Декодирует блок заголовков.
    /// </summary>
    /// <param name="block">Блок заголовков.</param>
    /// <returns>Коллекция заголовков.</returns>
    IEnumerable<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block);
}