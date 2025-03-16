using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет элемент, используемый в качестве источника для действия указателя.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ElementOrigin"/>.
/// </remarks>
/// <param name="element">Ссылка на элемент, который будет использоваться в качестве источника.</param>
public class ElementOrigin(SharedReference element)
{
    /// <summary>
    /// Тип источника.
    /// </summary>
    public string Type { get; } = "element";

    /// <summary>
    /// Ссылка на элемент, используемый в качестве источника.
    /// </summary>
    public SharedReference Element { get; } = element;
}