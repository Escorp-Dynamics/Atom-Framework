namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing event data for events raised when an unknown protocol message is received from a WebDriver Bidi connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UnknownMessageReceivedEventArgs"/> class.
/// </remarks>
/// <param name="message">The message received from the protocol.</param>
public class UnknownMessageReceivedEventArgs(string message) : EventArgs
{

    /// <summary>
    /// Gets the message received from the protocol.
    /// </summary>
    public string Message { get; } = message;
}