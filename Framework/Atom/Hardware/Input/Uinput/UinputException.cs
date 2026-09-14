namespace Atom.Hardware.Input.Uinput;

/// <summary>
/// Ошибка работы с <c lang="text">/dev/uinput</c>.
/// </summary>
public sealed class UinputException : Exception
{
    /// <inheritdoc cref="Exception()"/>
    public UinputException() { }

    /// <inheritdoc cref="Exception(string)"/>
    public UinputException(string message) : base(message) { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    public UinputException(string message, Exception innerException) : base(message, innerException) { }
}
