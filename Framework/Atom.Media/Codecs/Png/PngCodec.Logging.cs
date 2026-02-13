using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Высокопроизводительные методы логирования для PNG кодека.
/// Использует source generator для оптимизации.
/// </summary>
internal static partial class PngCodecLoggerExtensions
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "PNG {Mode} инициализирован: {Width}x{Height}")]
    public static partial void LogPngInitialized(this ILogger logger, string mode, int width, int height);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "PNG Encode: начало, {Width}x{Height}")]
    public static partial void LogPngEncodeStart(this ILogger logger, int width, int height);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Debug,
        Message = "PNG Encode: завершено, {Width}x{Height}, {BytesWritten} байт, {ElapsedMs:F2} мс")]
    public static partial void LogPngEncodeComplete(this ILogger logger, int width, int height, int bytesWritten, double elapsedMs);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Debug,
        Message = "PNG Decode: начало, входные данные {DataLength} байт")]
    public static partial void LogPngDecodeStart(this ILogger logger, int dataLength);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Debug,
        Message = "PNG Decode: завершено, {Width}x{Height}, {ElapsedMs:F2} мс")]
    public static partial void LogPngDecodeComplete(this ILogger logger, int width, int height, double elapsedMs);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Trace,
        Message = "PNG Headers: {Width}x{Height}, {BitDepth} bit, colorType={ColorType}")]
    public static partial void LogPngHeadersParsed(this ILogger logger, int width, int height, int bitDepth, int colorType);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Warning,
        Message = "PNG Error: {Message}")]
    public static partial void LogPngError(this ILogger logger, string message);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Trace,
        Message = "PNG Filter selected: {FilterType} for row {RowIndex}")]
    public static partial void LogPngFilterSelected(this ILogger logger, string filterType, int rowIndex);

    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Trace,
        Message = "PNG SIMD: using {SimdType} for {Operation}")]
    public static partial void LogPngSimdUsed(this ILogger logger, string simdType, string operation);
}
