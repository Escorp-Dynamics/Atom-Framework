using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Абстрактный базовый класс для прямоугольника обрезки для скриншота.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="ElementClipRectangle"/>.
/// </remarks>
/// <param name="element">Ссылка на элемент, используемый для обрезки скриншота.</param>
public class ElementClipRectangle(SharedReference element) : ClipRectangle()
{
    /// <summary>
    /// Тип прямоугольника обрезки.
    /// </summary>
    [JsonInclude]
    public override string Type => "element";

    /// <summary>
    /// Элемент, используемый для обрезки скриншота.
    /// </summary>
    [JsonInclude]
    public SharedReference Element { get; set; } = element;
}