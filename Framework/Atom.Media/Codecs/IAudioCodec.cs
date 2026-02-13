namespace Atom.Media;

/// <summary>
/// Интерфейс аудиокодека.
/// </summary>
public interface IAudioCodec : ICodec
{
    /// <summary>
    /// Параметры кодека.
    /// </summary>
    AudioCodecParameters Parameters { get; }

    /// <summary>
    /// Инициализирует декодер с параметрами.
    /// </summary>
    CodecResult InitializeDecoder(in AudioCodecParameters parameters);

    /// <summary>
    /// Инициализирует энкодер с параметрами.
    /// </summary>
    CodecResult InitializeEncoder(in AudioCodecParameters parameters);

    /// <summary>
    /// Декодирует пакет в кадр.
    /// </summary>
    CodecResult Decode(ReadOnlySpan<byte> packet, ref AudioFrame frame);

    /// <summary>
    /// Кодирует кадр в пакет.
    /// </summary>
    CodecResult Encode(in ReadOnlyAudioFrame frame, Span<byte> output, out int bytesWritten);

    /// <summary>
    /// Асинхронно декодирует пакет.
    /// </summary>
    ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        AudioFrameBuffer buffer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронно кодирует кадр.
    /// </summary>
    ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        AudioFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сбрасывает буфер декодера.
    /// </summary>
    CodecResult Flush(ref AudioFrame frame);
}
