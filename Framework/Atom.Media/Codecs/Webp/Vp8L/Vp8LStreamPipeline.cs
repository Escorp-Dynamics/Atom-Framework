#pragma warning disable S109, VSTHRD002, MA0182

using System.Buffers;

namespace Atom.Media;

/// <summary>
/// Потоковый VP8L pipeline с перекрытием encode/decode через double-buffering.
/// </summary>
/// <remarks>
/// <para>
/// Throughput = max(encode_time, decode_time) вместо encode + decode.
/// Задержка = +1 кадр (стандартная pipeline-латентность).
/// </para>
/// <para>
/// Модель работы:
/// <list type="bullet">
///   <item>ProcessFrame(input) — кодирует input на фоновом потоке, декодирует предыдущий на текущем</item>
///   <item>Flush(output) — декодирует последний закодированный кадр</item>
///   <item>Первый вызов не возвращает декодированный кадр (hasOutput=false)</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class Vp8LStreamPipeline : IDisposable
{
    /// <summary>Managed буфер для пикселей encoder-input (копия из ReadOnlyVideoFrame).</summary>
    private uint[]? pixelBuf;

    /// <summary>Double-buffered encoded data: один кодируется, другой декодируется.</summary>
    private byte[]? encodeBuf;
    private byte[]? decodeBuf;
    private int decodeBufLen;

    /// <summary>Фоновая задача кодирования.</summary>
    private Task<(CodecResult Result, int Length)>? pendingEncode;

    /// <summary>Reusable декодер (переиспользует внутренние буферы).</summary>
    private Vp8LDecoder? decoder;

    private int width;
    private int height;
    private bool hasAlpha;
    private bool disposed;

    /// <summary>
    /// Прогревает JIT encode/decode путей. Обрабатывает несколько кадров через полный pipeline и сбрасывает.
    /// </summary>
    public void Warmup(in ReadOnlyVideoFrame input, ref VideoFrame output, int iterations = 2)
    {
        for (var i = 0; i < iterations; i++)
            ProcessFrame(input, ref output, out _);
        Flush(ref output);
    }

    /// <summary>
    /// Кодирует входной кадр на фоновом потоке, одновременно декодирует предыдущий на текущем.
    /// </summary>
    /// <param name="input">Входной кадр (RGB24 или RGBA32).</param>
    /// <param name="output">Буфер для декодированного кадра (заполняется только при hasOutput=true).</param>
    /// <param name="hasOutput">true если output содержит декодированный кадр (false на первом вызове).</param>
    /// <returns>Результат операции.</returns>
    public CodecResult ProcessFrame(in ReadOnlyVideoFrame input, ref VideoFrame output, out bool hasOutput)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var w = input.Width;
        var h = input.Height;
        var alpha = input.PixelFormat == VideoPixelFormat.Rgba32;
        var totalPixels = w * h;

        EnsureBuffers(w, h, alpha, totalPixels);

        // Копируем пиксели в managed буфер (sync — ref struct не может быть захвачен лямбдой)
        Vp8LEncoder.CopyFramePixels(input, pixelBuf!, totalPixels, alpha);

        // Запускаем кодирование текущего кадра на фоновом потоке
        var pix = pixelBuf!;
        var enc = encodeBuf!;
        var newEncode = Task.Run(() =>
        {
            var r = Vp8LEncoder.EncodeFromPixels(pix, w, h, alpha, enc, out var l);
            return (r, l);
        });

        // Пока кодируется — декодируем ПРЕДЫДУЩИЙ кадр на текущем потоке
        hasOutput = false;
        var decodeResult = CodecResult.Success;
        if (decodeBufLen > 0)
        {
            decoder ??= new Vp8LDecoder();
            var chunkSize = BitConverter.ToInt32(decodeBuf!, 16);
            decodeResult = decoder.Decode(decodeBuf.AsSpan(20, chunkSize), ref output);
            hasOutput = decodeResult == CodecResult.Success;
        }

        // Ждём завершения кодирования
        var (encResult, encLen) = newEncode.GetAwaiter().GetResult();
        if (encResult != CodecResult.Success)
            return encResult;

        // Swap: encodeBuf (только что закодирован) → decodeBuf (будет декодирован в следующем вызове)
        (encodeBuf, decodeBuf) = (decodeBuf, encodeBuf);
        decodeBufLen = encLen;

        return decodeResult;
    }

    /// <summary>
    /// Декодирует последний закодированный кадр (вызывать после последнего ProcessFrame).
    /// </summary>
    public CodecResult Flush(ref VideoFrame output)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (pendingEncode is not null)
        {
            var (r, l) = pendingEncode.GetAwaiter().GetResult();
            pendingEncode = null;
            if (r != CodecResult.Success) return r;
            (encodeBuf, decodeBuf) = (decodeBuf, encodeBuf);
            decodeBufLen = l;
        }

        if (decodeBufLen <= 0)
            return CodecResult.Success;

        decoder ??= new Vp8LDecoder();
        var chunkSize = BitConverter.ToInt32(decodeBuf!, 16);
        var result = decoder.Decode(decodeBuf.AsSpan(20, chunkSize), ref output);
        decodeBufLen = 0;
        return result;
    }

    private void EnsureBuffers(int w, int h, bool alpha, int totalPixels)
    {
        if (width == w && height == h && hasAlpha == alpha)
            return;

        FreeBuffers();

        width = w;
        height = h;
        hasAlpha = alpha;

        var maxEncodedSize = (totalPixels * 10) + 4096;
        pixelBuf = ArrayPool<uint>.Shared.Rent(totalPixels);
        encodeBuf = ArrayPool<byte>.Shared.Rent(maxEncodedSize);
        decodeBuf = ArrayPool<byte>.Shared.Rent(maxEncodedSize);
    }

    private void FreeBuffers()
    {
        if (pixelBuf is not null) { ArrayPool<uint>.Shared.Return(pixelBuf); pixelBuf = null; }
        if (encodeBuf is not null) { ArrayPool<byte>.Shared.Return(encodeBuf); encodeBuf = null; }
        if (decodeBuf is not null) { ArrayPool<byte>.Shared.Return(decodeBuf); decodeBuf = null; }
        decodeBufLen = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (pendingEncode is { } pending)
        {
            pending.GetAwaiter().GetResult();
            pendingEncode = null;
        }

        decoder?.Dispose();
        decoder = null;
        FreeBuffers();
    }
}
