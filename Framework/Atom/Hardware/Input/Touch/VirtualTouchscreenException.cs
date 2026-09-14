namespace Atom.Hardware.Input.Touch;

/// <summary>
/// Ошибка виртуального тачскрина.
/// </summary>
public sealed class VirtualTouchscreenException : Exception
{
    /// <inheritdoc cref="Exception()"/>
    public VirtualTouchscreenException() { }

    /// <inheritdoc cref="Exception(string)"/>
    public VirtualTouchscreenException(string message) : base(message) { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    public VirtualTouchscreenException(string message, Exception innerException) : base(message, innerException) { }
}
