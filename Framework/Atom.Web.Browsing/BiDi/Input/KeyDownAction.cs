namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для отправки нажатия клавиши на устройстве ввода с клавиатуры.
/// </summary>
public class KeyDownAction : IKeySourceAction
{
    /// <summary>
    /// Получает тип действия.
    /// </summary>
    public string Type { get; } = "keyDown";

    /// <summary>
    /// Получает или задает значение действия нажатия клавиши.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="KeyDownAction"/>.
    /// </summary>
    /// <param name="value">Текст клавиш для отправки при нажатии.</param>
    public KeyDownAction(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Значение действия не может быть null или пустой строкой", nameof(value));
        Value = value;
    }
}