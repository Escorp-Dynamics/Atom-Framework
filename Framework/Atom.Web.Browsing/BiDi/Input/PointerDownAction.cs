using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для отправки нажатия кнопки на устройстве указателя.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="PointerDownAction"/>.
/// </remarks>
/// <param name="button">Кнопка, используемая для нажатия.</param>
public class PointerDownAction(long button) : PointerAction(), IPointerSourceAction
{
    /// <summary>
    /// Тип действия.
    /// </summary>
    public string Type { get; } = "pointerDown";

    /// <summary>
    /// Кнопка, которая будет нажата.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long Button { get; set; } = button;
}