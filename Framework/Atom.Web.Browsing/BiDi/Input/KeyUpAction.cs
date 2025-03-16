namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для отправки отпускания клавиши на устройстве ввода с клавиатуры.
/// </summary>
public class KeyUpAction : IKeySourceAction
{
    /// <summary>
    /// Тип действия.
    /// </summary>
    public string Type { get; } = "keyUp";

    /// <summary>
    /// Значение действия отпускания клавиши.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="KeyUpAction"/>.
    /// </summary>
    /// <param name="value">Текст клавиш для отправки при отпускании.</param>
    public KeyUpAction(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Значение действия не может быть null или пустой строкой", nameof(value));
        Value = value;
    }
}