namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing event data for events raised when data is received from a WebDriver Bidi connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ConnectionDataReceivedEventArgs"/> class.
/// </remarks>
/// <param name="data">The data received from the connection.</param>
public class ConnectionDataReceivedEventArgs(ReadOnlyMemory<byte> data) : BiDiEventArgs
{

    /// <summary>
    /// Gets the data received from the connection.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; } = data;
}