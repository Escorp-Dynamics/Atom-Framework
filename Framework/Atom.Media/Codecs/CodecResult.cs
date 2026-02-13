#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Результат операции кодирования/декодирования.
/// </summary>
public enum CodecResult : byte
{
    /// <summary>Операция успешна.</summary>
    Success = 0,

    /// <summary>Декодеру нужно больше данных для вывода кадра.</summary>
    NeedMoreData = 1,

    /// <summary>Выходной буфер слишком мал.</summary>
    OutputBufferTooSmall = 2,

    /// <summary>Входные данные повреждены или невалидны.</summary>
    InvalidData = 3,

    /// <summary>Достигнут конец потока.</summary>
    EndOfStream = 4,

    /// <summary>Кодек не инициализирован.</summary>
    NotInitialized = 5,

    /// <summary>Неподдерживаемый формат.</summary>
    UnsupportedFormat = 6,

    /// <summary>Операция отменена.</summary>
    Cancelled = 7,

    /// <summary>Неизвестная ошибка.</summary>
    Error = 255,
}

/// <summary>
/// Расширения для <see cref="CodecResult"/>.
/// </summary>
public static class CodecResultExtensions
{
    /// <summary>
    /// Возвращает true, если операция успешна.
    /// </summary>
    public static bool IsSuccess(this CodecResult result) => result is CodecResult.Success;

    /// <summary>
    /// Возвращает true, если это ошибка (не Success и не NeedMoreData).
    /// </summary>
    public static bool IsError(this CodecResult result)
        => result is not CodecResult.Success and not CodecResult.NeedMoreData and not CodecResult.EndOfStream;

    /// <summary>
    /// Выбрасывает исключение, если результат — ошибка.
    /// </summary>
    public static void ThrowIfError(this CodecResult result, string? message = null)
    {
        if (result.IsError())
            throw new CodecException(result, message ?? $"Codec operation failed: {result}");
    }
}
