using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Tests;

/// <summary>
/// Унифицированный интерфейс кодека для кросс‑проверок.
/// </summary>
public interface ICodec
{
    /// <summary>
    /// Читаемое имя кодека (для осмысленных названий тесткейсов).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Сжимает данные.
    /// </summary>
    /// <param name="src">Исходные данные.</param>
    /// <param name="level">Уровень сжатия (обычно 1..22; можно игнорировать, если кодек не поддерживает).</param>
    /// <param name="withChecksum">Надо ли добавлять checksum кадра (если поддерживается).</param>
    /// <returns>Буфер сжатых данных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte[] Compress(ReadOnlySpan<byte> src, int level);

    /// <summary>
    /// Распаковывает данные.
    /// </summary>
    /// <param name="compressed">Сжатые данные.</param>
    /// <returns>Распакованные байты 1:1 к исходнику.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte[] Decompress(ReadOnlySpan<byte> compressed);

    /// <summary>
    /// Стриминговая проверка: компрессия из потока с малым буфером, далее декомпрессия потока и верификация.
    /// Если кодек не поддерживает поток — можно реализовать обёртки поверх MemoryStream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void CompressStream(System.IO.Stream src, System.IO.Stream dst, int level, int ioChunk);

    /// <summary>
    /// Стриминговая декомпрессия (в паре к CompressStream).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void DecompressStream(System.IO.Stream src, System.IO.Stream dst, int ioChunk);
}