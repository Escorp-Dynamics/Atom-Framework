namespace Atom.Media.Audio;

/// <summary>
/// Представляет исключение виртуального микрофона.
/// </summary>
public class VirtualMicrophoneException : NativeException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMicrophoneException"/>.
    /// </summary>
    public VirtualMicrophoneException() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMicrophoneException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VirtualMicrophoneException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMicrophoneException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VirtualMicrophoneException(string? message) : base(message) { }
}
