#pragma warning disable CA1720
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет источник действия.
/// </summary>
public class Origin
{
    /// <summary>
    /// Значение источника действия.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Источник действия для области просмотра браузера.
    /// </summary>
    public static Origin Viewport => new("viewport");

    /// <summary>
    /// Источник действия для текущей позиции указателя.
    /// </summary>
    public static Origin Pointer => new("pointer");

    private Origin(string originValue) => Value = originValue;

    private Origin(ElementOrigin originValue) => Value = originValue;

    /// <summary>
    /// Создает источник действия с использованием ссылки на элемент.
    /// </summary>
    /// <param name="originValue">Точка источника элемента.</param>
    /// <returns>Источник действия для указанной ссылки на элемент.</returns>
    public static Origin Element(ElementOrigin originValue) => new(originValue);

    /// <summary>
    /// Создает источник действия с использованием ссылки на элемент.
    /// </summary>
    /// <param name="elementReference">Объект SharedReference, содержащий ссылку на элемент.</param>
    /// <returns>Источник действия для указанной ссылки на элемент.</returns>
    public static Origin Element(SharedReference elementReference) => new(new ElementOrigin(elementReference));
}