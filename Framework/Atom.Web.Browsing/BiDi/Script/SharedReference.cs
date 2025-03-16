using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing a remote reference to an object in the browser containing a shared ID, such as a node.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SharedReference"/> class.
/// </remarks>
/// <param name="sharedId">The shared ID of the remote object.</param>
public class SharedReference(string sharedId) : RemoteReference(null, sharedId)
{
    /// <summary>
    /// Gets or sets the shared ID of the remote object.
    /// </summary>
    [JsonIgnore]
    public string SharedId
    {
        get => InternalSharedId!;
        set => InternalSharedId = value;
    }

    /// <summary>
    /// Gets or sets the handle of the remote object.
    /// </summary>
    [JsonIgnore]
    public string? Handle
    {
        get => InternalHandle!;
        set => InternalHandle = value;
    }
}