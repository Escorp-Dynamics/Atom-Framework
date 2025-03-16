using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет локатор для поиска узлов по их видимому тексту, определённому свойством DOM innerText.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="InnerTextLocator"/>.
/// </remarks>
/// <param name="value">Текст для поиска узлов.</param>
public class InnerTextLocator(string value) : Locator()
{
    private readonly string type = "innerText";
    private readonly string value = value;

    /// <summary>
    /// Тип локатора.
    /// </summary>
    public override string Type => type;

    /// <summary>
    /// Текст для поиска узлов.
    /// </summary>
    public override object Value => value;

    /// <summary>
    /// Указывает, следует ли игнорировать регистр при сопоставлении. Если опущено или <see langword="null"/>, сопоставление чувствительно к регистру.
    /// </summary>
    [JsonPropertyName("ignoreCase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsIgnoreCase { get; set; }

    /// <summary>
    /// Тип сопоставления текста: частичное или полное. Если опущено или <see langword="null"/>, сопоставление выполняется по полному тексту.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InnerTextMatchType? MatchType { get; set; }

    /// <summary>
    /// Максимальная глубина поиска узлов. Если опущено или <see langword="null"/>, локатор будет возвращать совпадения на бесконечной глубине.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? MaxDepth { get; set; }
}