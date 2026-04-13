namespace Atom.Hardware.Display;

/// <summary>
/// Представляет исключение виртуального дисплея.
/// </summary>
public class VirtualDisplayException : NativeException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualDisplayException"/>.
    /// </summary>
    public VirtualDisplayException() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualDisplayException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VirtualDisplayException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualDisplayException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VirtualDisplayException(string? message) : base(message) { }
}
