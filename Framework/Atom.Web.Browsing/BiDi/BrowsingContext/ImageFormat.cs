using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет формат изображения для захваченного скриншота.
/// </summary>
public class ImageFormat
{
    /// <summary>
    /// MIME-тип формата изображения. По умолчанию "image/png".
    /// </summary>
    public string Type { get; set; } = "image/png";

    /// <summary>
    /// Качество формата изображения. Если указано, должно быть в диапазоне от 0 до 1 включительно.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Quality
    {
        get;

        set
        {
            if (value is < 0 or > 1) throw new BiDiException("Качество должно быть в диапазоне от 0 до 1 включительно");
            field = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ImageFormat"/>.
    /// </summary>
    public ImageFormat() { }
}