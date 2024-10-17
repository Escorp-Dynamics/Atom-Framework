namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет узел CDATASection.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CDATASection"/>.
/// </remarks>
/// <param name="data">Данные узла.</param>
public class CDATASection(string data) : Text(data), ICDATASection
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CDATASection"/>.
    /// </summary>
    public CDATASection() : this(string.Empty) { }
}