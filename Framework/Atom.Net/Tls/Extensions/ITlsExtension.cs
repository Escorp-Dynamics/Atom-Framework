namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Представляет базовый интерфейс для реализации расширений TLS.
/// </summary>
public interface ITlsExtension
{
    /// <summary>
    /// Уникальный идентификатор расширения (например, 0x0010 для ALPN).
    /// </summary>
    ushort Id { get; }

    /// <summary>
    /// Итоговый размер расширения в байтах.
    /// </summary>
    int Size { get; }

    /// <summary>
    /// Пишет расширение в указанный буфер.
    /// </summary>
    /// <param name="buffer">Буфер назначения.</param>
    /// <param name="offset">Начальная позиция.</param>
    void Write(Span<byte> buffer, ref int offset);
}