using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет базовый интерфейс для реализации кодировщиков заголовков.
/// </summary>
public interface IHeadersEncoder
{
    /// <summary>
    /// Определяет или задаёт, будет ли использовано кодирование Хаффмана.
    /// </summary>
    bool UseHuffman { get; set; }

    /// <summary>
    /// Кодирует заголовки в буфер записи.
    /// </summary>
    /// <param name="writer">Буфер записи.</param>
    /// <param name="headers">Заголовки.</param>
    void Encode(IBufferWriter<byte> writer, [NotNull] IEnumerable<KeyValuePair<string, string>> headers);
}