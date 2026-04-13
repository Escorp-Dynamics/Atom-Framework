namespace Atom.Hardware.Input;

/// <summary>
/// Представляет исключение виртуальной мыши.
/// </summary>
public class VirtualMouseException : NativeException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMouseException"/>.
    /// </summary>
    public VirtualMouseException() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMouseException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VirtualMouseException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualMouseException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VirtualMouseException(string? message) : base(message) { }
}
