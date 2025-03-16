using System.Collections.ObjectModel;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// A read-only list of RemoteValue objects.
/// </summary>
public class RemoteValueList : ReadOnlyCollection<RemoteValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteValueList"/> class.
    /// </summary>
    /// <param name="list">The list of RemoteValue objects to wrap as read-only.</param>
    internal RemoteValueList(List<RemoteValue> list) : base(list) { }
}