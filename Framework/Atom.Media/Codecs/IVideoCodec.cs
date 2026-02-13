namespace Atom.Media;

/// <summary>
/// Интерфейс видеокодека.
/// </summary>
public interface IVideoCodec : ICodec
{
    /// <summary>
    /// Параметры кодека.
    /// </summary>
    VideoCodecParameters Parameters { get; }

    /// <summary>
    /// Инициализирует декодер с параметрами.
    /// </summary>
    CodecResult InitializeDecoder(in VideoCodecParameters parameters);

    /// <summary>
    /// Инициализирует энкодер с параметрами.
    /// </summary>
    CodecResult InitializeEncoder(in VideoCodecParameters parameters);

    /// <summary>
    /// Декодирует пакет в кадр.
    /// </summary>
    /// <param name="packet">Закодированные данные.</param>
    /// <param name="frame">Выходной кадр.</param>
    /// <returns>Результат декодирования.</returns>
    CodecResult Decode(ReadOnlySpan<byte> packet, ref VideoFrame frame);

    /// <summary>
    /// Кодирует кадр в пакет.
    /// </summary>
    /// <param name="frame">Входной кадр.</param>
    /// <param name="output">Буфер для закодированных данных.</param>
    /// <param name="bytesWritten">Количество записанных байт.</param>
    /// <returns>Результат кодирования.</returns>
    CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten);

    /// <summary>
    /// Асинхронно декодирует пакет.
    /// </summary>
    ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        VideoFrameBuffer buffer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронно кодирует кадр.
    /// </summary>
    ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        VideoFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сбрасывает буфер декодера и получает оставшиеся кадры.
    /// </summary>
    CodecResult Flush(ref VideoFrame frame);
}
