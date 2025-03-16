using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет прямоугольник обрезки для скриншота.
/// </summary>
public class BoxClipRectangle : ClipRectangle
{
    /// <summary>
    /// Тип прямоугольника обрезки.
    /// </summary>
    public override string Type => "box";

    /// <summary>
    /// Координата X прямоугольника обрезки относительно левого края области просмотра.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double X { get; set; }

    /// <summary>
    /// Координата Y прямоугольника обрезки относительно верхнего края области просмотра.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double Y { get; set; }

    /// <summary>
    /// Ширина прямоугольника обрезки.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double Width { get; set; }

    /// <summary>
    /// Высота прямоугольника обрезки.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double Height { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BoxClipRectangle"/>.
    /// </summary>
    public BoxClipRectangle() : base() { }
}