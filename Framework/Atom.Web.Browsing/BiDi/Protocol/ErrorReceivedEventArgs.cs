namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing event data for events raised when a protocol error is received from a WebDriver Bidi connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ErrorReceivedEventArgs"/> class.
/// </remarks>
/// <param name="errorData">The data about the error received from the connection.</param>
public class ErrorReceivedEventArgs(ErrorResult? errorData) : EventArgs
{
    /// <summary>
    /// Gets the error response data.
    /// </summary>
    public ErrorResult? ErrorData { get; } = errorData;
}