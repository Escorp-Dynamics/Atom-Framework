using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие указателя.
/// </summary>
public class PointerAction
{
    /// <summary>
    /// Ширина указателя в пикселях. Если не указано, по умолчанию равно 1.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Width { get; set; }

    /// <summary>
    /// Высота указателя в пикселях. Если не указано, по умолчанию равно 1.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Height { get; set; }

    /// <summary>
    /// Давление указателя на поверхность. Если не указано, по умолчанию равно 0.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? Pressure { get; set; }

    /// <summary>
    /// Тангенциальное давление указателя на поверхность. Если не указано, по умолчанию равно 0.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? TangentialPressure { get; set; }

    /// <summary>
    /// Поворот указателя в градусах, от 0 до 359, на поверхности. Если не указано, по умолчанию равно 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Twist
    {
        get;

        set
        {
            if (value > 359) throw new BiDiException("Значение Twist должно быть между 0 и 359");
            field = value;
        }
    }

    /// <summary>
    /// Угол наклона (угол от горизонтали) устройства указателя. Должен быть между 0 и 1.5707963267948966 (pi / 2). Если не указано, по умолчанию равно 0.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? AltitudeAngle
    {
        get;

        set
        {
            if (value is < 0 or > (Math.PI / 2)) throw new BiDiException("Значение AltitudeAngle должно быть между 0 и 1.5707963267948966 (pi / 2) включительно");
            field = value;
        }
    }

    /// <summary>
    /// Азимутальный угол (угол от «севера» или линии, направленной вверх от точки контакта) устройства указателя. Должен быть между 0 и 6.283185307179586 (2 * pi). Если не указано, по умолчанию равно 0.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double? AzimuthAngle
    {
        get;

        set
        {
            if (value is < 0 or > (Math.PI * 2)) throw new BiDiException("Значение AzimuthAngle должно быть между 0 и 6.283185307179586 (2 * pi) включительно");
            field = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PointerAction"/>.
    /// </summary>
    protected PointerAction() { }
}