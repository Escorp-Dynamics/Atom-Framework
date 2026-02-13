using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Параметры кодека изображения.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ImageCodecParameters
{
    /// <summary>
    /// Ширина изображения.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Высота изображения.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Формат пикселей.
    /// </summary>
    public required VideoPixelFormat PixelFormat { get; init; }

    /// <summary>
    /// Качество сжатия (0-100, для lossy форматов).
    /// </summary>
    public int Quality { get; init; }

    /// <summary>
    /// Уровень сжатия (0-9, для lossless форматов типа PNG).
    /// </summary>
    public int CompressionLevel { get; init; }

    /// <summary>
    /// Использовать быструю фильтрацию (фиксированный фильтр вместо адаптивного).
    /// Значительно ускоряет кодирование, но может увеличить размер файла.
    /// </summary>
    public bool FastFiltering { get; init; }

    /// <summary>
    /// Создаёт параметры с настройками по умолчанию.
    /// </summary>
    public static ImageCodecParameters Default => new()
    {
        Width = 0,
        Height = 0,
        PixelFormat = VideoPixelFormat.Rgb24,
        Quality = 90,
        CompressionLevel = 6,
        FastFiltering = false,
    };

    /// <summary>
    /// Создаёт параметры для изображения.
    /// </summary>
    [SetsRequiredMembers]
    public ImageCodecParameters(int width, int height, VideoPixelFormat pixelFormat)
    {
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        Quality = 90;
        CompressionLevel = 6;
    }
}

/// <summary>
/// Интерфейс кодека изображений.
/// </summary>
/// <remarks>
/// Предназначен для кодирования/декодирования статических изображений
/// (BMP, PNG, JPEG, GIF и др.). В отличие от видеокодеков,
/// изображения обрабатываются без временных зависимостей между кадрами.
/// </remarks>
public interface IImageCodec : ICodec
{
    /// <summary>
    /// Параметры кодека.
    /// </summary>
    ImageCodecParameters Parameters { get; }

    /// <summary>
    /// Поддерживаемые расширения файлов.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// MIME-тип формата.
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Инициализирует декодер с параметрами.
    /// </summary>
    CodecResult InitializeDecoder(ImageCodecParameters parameters);

    /// <summary>
    /// Инициализирует энкодер с параметрами.
    /// </summary>
    CodecResult InitializeEncoder(ImageCodecParameters parameters);

    /// <summary>
    /// Декодирует изображение из данных.
    /// </summary>
    /// <param name="data">Закодированные данные изображения.</param>
    /// <param name="frame">Выходной кадр с декодированным изображением.</param>
    /// <returns>Результат декодирования.</returns>
    CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame);

    /// <summary>
    /// Кодирует изображение в указанный формат.
    /// </summary>
    /// <param name="frame">Входной кадр с изображением.</param>
    /// <param name="output">Буфер для закодированных данных.</param>
    /// <param name="bytesWritten">Количество записанных байт.</param>
    /// <returns>Результат кодирования.</returns>
    CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten);

    /// <summary>
    /// Асинхронно декодирует изображение.
    /// </summary>
    ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> data,
        VideoFrameBuffer buffer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронно кодирует изображение.
    /// </summary>
    ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        VideoFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Определяет формат изображения по заголовку.
    /// </summary>
    /// <param name="header">Первые байты файла.</param>
    /// <returns>True, если формат соответствует этому кодеку.</returns>
    new bool CanDecode(ReadOnlySpan<byte> header);

    /// <summary>
    /// Вычисляет примерный размер закодированного изображения.
    /// </summary>
    int EstimateEncodedSize(int width, int height, VideoPixelFormat format);
}
