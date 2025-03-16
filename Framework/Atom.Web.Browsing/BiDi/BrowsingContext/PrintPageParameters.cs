using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры размера страницы для печати.
/// </summary>
public class PrintPageParameters
{
    private const string ErrorMessage = "Значение должно быть больше или равно нулю";

    /// <summary>
    /// Ширина страницы в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 21.59.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? Width
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Высота страницы в сантиметрах для печати. Значение должно быть больше или равно нулю. Если опущено, по умолчанию используется 27.94.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? Height
    {
        get;

        set
        {
            if (value is not null && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value), ErrorMessage);
            field = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PrintPageParameters"/>.
    /// </summary>
    public PrintPageParameters() { }
}