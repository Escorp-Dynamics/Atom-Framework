using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing a remote reference to an existing ECMAScript object in the browser.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RemoteObjectReference"/> class.
/// </remarks>
/// <param name="handle">The handle of the remote object.</param>
public class RemoteObjectReference(string handle) : RemoteReference(handle, null)
{
    /// <summary>
    /// Gets or sets the handle of the remote object.
    /// </summary>
    [JsonIgnore]
    public string Handle
    {
        get => InternalHandle!;
        set => InternalHandle = value;
    }

    /// <summary>
    /// Gets or sets the shard ID of the remote object.
    /// </summary>
    [JsonIgnore]
    public string? SharedId
    {
        get => InternalSharedId;
        set => InternalSharedId = value;
    }
}