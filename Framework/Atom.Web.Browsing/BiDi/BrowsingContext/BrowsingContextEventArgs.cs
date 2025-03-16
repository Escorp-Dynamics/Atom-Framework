namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет данные события, возникающего при создании или уничтожении контекста просмотра.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="BrowsingContextEventArgs"/>.
/// </remarks>
/// <param name="info">Объект <see cref="BrowsingContextInfo"/>, используемый для создания аргументов события.</param>
public class BrowsingContextEventArgs(BrowsingContextInfo info) : BiDiEventArgs
{
    private readonly BrowsingContextInfo info = info;

    /// <summary>
    /// Идентификатор контекста просмотра.
    /// </summary>
    public string BrowsingContextId => info.BrowsingContextId;

    /// <summary>
    /// Идентификатор пользовательского контекста для контекста просмотра.
    /// </summary>
    public string UserContextId => info.UserContextId;

    /// <summary>
    /// Идентификатор контекста просмотра, который изначально открыл этот контекст просмотра.
    /// </summary>
    public string? OriginalOpener => info.OriginalOpener;

    /// <summary>
    /// Текущий URL контекста просмотра.
    /// </summary>
    public Uri Url => info.Url;

    /// <summary>
    /// Коллекция дочерних контекстов просмотра для этого контекста просмотра.
    /// </summary>
    public IEnumerable<BrowsingContextInfo> Children => info.Children;

    /// <summary>
    /// Идентификатор родительского контекста просмотра.
    /// </summary>
    public string? Parent => info.Parent;
}