namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// A read-only header from a request.
/// </summary>
public class ReadOnlyHeader
{
    private readonly Header header;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadOnlyHeader"/> class.
    /// </summary>
    /// <param name="header">The header to make read-only.</param>
    internal ReadOnlyHeader(Header header) => this.header = header;

    /// <summary>
    /// Gets the name of the header.
    /// </summary>
    public string Name => header.Name;

    /// <summary>
    /// Gets the value of the header.
    /// </summary>
    public BytesValue Value => header.Value;
}