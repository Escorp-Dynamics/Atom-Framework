namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing event data for the event raised when a script realm is destroyed.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RealmCreatedEventArgs"/> class.
/// </remarks>
/// <param name="info">The RealmInfo object containing information about the realm being created.</param>
public class RealmCreatedEventArgs(RealmInfo info) : BiDiEventArgs
{
    private readonly RealmInfo info = info;

    /// <summary>
    /// Gets the ID of the realm being created.
    /// </summary>
    public string RealmId => info.RealmId;

    /// <summary>
    /// Gets the origin of the realm being created.
    /// </summary>
    public string Origin => info.Origin;

    /// <summary>
    /// Gets the type of the realm being created.
    /// </summary>
    public RealmType Type => info.Type;

    /// <summary>
    /// Gets this RealmCreatedEventArgs instance as a RealmInfo containing type-specific realm info.
    /// </summary>
    /// <typeparam name="T">The specific type of RealmInfo to return.</typeparam>
    /// <returns>This RealmCreatedEventArgs instance cast to the specified correct type.</returns>
    /// <exception cref="BiDiException">Thrown if this RealmInfo is not the specified type.</exception>
    public T As<T>() where T : RealmInfo => info.As<T>();
}