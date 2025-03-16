using System.Collections.ObjectModel;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// A read-only dictionary of RemoteValue objects.
/// </summary>
public class RemoteValueDictionary : ReadOnlyDictionary<object, RemoteValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteValueDictionary"/> class.
    /// </summary>
    /// <param name="dictionary">The dictionary of RemoteValue objects to wrap as read-only.</param>
    internal RemoteValueDictionary(Dictionary<object, RemoteValue> dictionary) : base(dictionary) { }
}