namespace Atom.Media.Video;

/// <summary>
/// Представляет исключение виртуальной камеры.
/// </summary>
public class VirtualCameraException : Exception
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualCameraException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VirtualCameraException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualCameraException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VirtualCameraException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualCameraException"/>.
    /// </summary>
    public VirtualCameraException() : base() { }
}