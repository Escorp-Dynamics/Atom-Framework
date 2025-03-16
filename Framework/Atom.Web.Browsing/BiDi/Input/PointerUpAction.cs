using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для отправки отпускания кнопки на устройстве указателя.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="PointerUpAction"/>.
/// </remarks>
/// <param name="button">Кнопка, используемая для отпускания.</param>
public class PointerUpAction(long button) : object(), IPointerSourceAction
{
    /// <summary>
    /// Тип действия.
    /// </summary>
    public string Type { get; } = "pointerUp";

    /// <summary>
    /// Кнопка, которая будет отпущена.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long Button { get; set; } = button;
}