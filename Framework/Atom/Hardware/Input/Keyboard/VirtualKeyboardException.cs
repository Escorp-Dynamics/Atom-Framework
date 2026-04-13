namespace Atom.Hardware.Input;

/// <summary>
/// Представляет исключение виртуальной клавиатуры.
/// </summary>
public class VirtualKeyboardException : NativeException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualKeyboardException"/>.
    /// </summary>
    public VirtualKeyboardException() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualKeyboardException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VirtualKeyboardException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualKeyboardException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VirtualKeyboardException(string? message) : base(message) { }
}
