namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет комментарий.
/// </summary>
public class Comment : CharacterData, IComment
{
    internal Comment(Uri baseURI, string data) : base(baseURI, "comment", data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Comment"/>.
    /// </summary>
    /// <param name="data">Данные комментария.</param>
    public Comment(string data) : this(new Uri("about:blank"), data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Comment"/>.
    /// </summary>
    public Comment() : this(string.Empty) { }
}