namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет исключение, возникшее при взаимодействии с мостом браузера.
/// </summary>
internal class BridgeException : Exception
{
    public BridgeException(string? message, Exception? innerException) : base(message, innerException) { }

    public BridgeException(string? message) : base(message) { }

    public BridgeException() { }
}