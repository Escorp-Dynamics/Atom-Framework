using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры полей для печати.
/// </summary>
public class PrintMarginParameters
{
    private const string ErrorMessage = "Значение должно быть больше или равно нулю";

    /// <summary>
    /// Левое поле в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 1.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Left
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Правое поле в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 1.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Right
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Верхнее поле в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 1.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Top
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Нижнее поле в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 1.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Bottom
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PrintMarginParameters"/>.
    /// </summary>
    public PrintMarginParameters() { }
}