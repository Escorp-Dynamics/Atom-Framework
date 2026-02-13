namespace Atom.Media;

/// <summary>
/// Исключение операции кодека.
/// </summary>
public sealed class CodecException : Exception
{
    /// <summary>
    /// Результат операции.
    /// </summary>
    public CodecResult Result { get; }

    /// <summary>
    /// Создаёт исключение по умолчанию.
    /// </summary>
    public CodecException() : base("Codec error") { }

    /// <summary>
    /// Создаёт исключение с сообщением.
    /// </summary>
    public CodecException(string message) : base(message) { }

    /// <summary>
    /// Создаёт исключение с сообщением и внутренним исключением.
    /// </summary>
    public CodecException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Создаёт исключение с результатом.
    /// </summary>
    public CodecException(CodecResult result, string? message = null)
        : base(message ?? $"Codec error: {result}") => Result = result;

    /// <summary>
    /// Создаёт исключение с результатом и внутренним исключением.
    /// </summary>
    public CodecException(CodecResult result, string? message, Exception? innerException)
        : base(message ?? $"Codec error: {result}", innerException) => Result = result;
}
