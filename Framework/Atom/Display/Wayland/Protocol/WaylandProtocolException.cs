namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Нарушение протокола Wayland.
/// </summary>
public sealed class WaylandProtocolException : Exception
{
    /// <inheritdoc cref="Exception()"/>
    public WaylandProtocolException() { }

    /// <inheritdoc cref="Exception(string)"/>
    public WaylandProtocolException(string message) : base(message) { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    public WaylandProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
