using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.navigate.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="PrintCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра для печати.</param>
public class PrintCommandParameters(string browsingContextId) : CommandParameters<PrintCommandResult>
{
    [JsonPropertyName("pageRanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal IList<object>? SerializablePageRanges
    {
        get
        {
            if (!PageRanges.Any()) return default;

            var serializable = new List<object>();

            foreach (var pageRange in PageRanges)
            {
                if (pageRange is string or long or int or short)
                    serializable.Add(pageRange);
                else
                    throw new BiDiException("Диапазон страниц должен быть строкой или целым числом");
            }

            return serializable;
        }
    }

    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.print";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого делается скриншот.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Указывает, следует ли печатать фоновые изображения. По умолчанию false.
    /// </summary>
    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsBackground { get; set; }

    /// <summary>
    /// Поля для печатаемой страницы.
    /// </summary>
    [JsonPropertyName("margin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrintMarginParameters? Margins { get; set; }

    /// <summary>
    /// Ориентация печатаемой страницы. Если опущено, по умолчанию используется Portrait.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrintOrientation? Orientation { get; set; }

    /// <summary>
    /// Получает или задаёт значение, содержащее информацию о размере печатаемой страницы.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrintPageParameters? Page { get; set; }

    /// <summary>
    /// Коэффициент масштабирования печатаемой страницы. Значение должно быть в диапазоне от 0.1 до 2.0 включительно. Если опущено, по умолчанию используется 1.0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale
    {
        get;

        set
        {
            if (value is not null && (value.Value is < .1 or > 2)) throw new ArgumentOutOfRangeException(nameof(value), "Значение должно быть в диапазоне от 0.1 до 2.0");
            field = value;
        }
    }

    /// <summary>
    /// Указывает, следует ли сжимать содержимое для размещения на одной странице. Если опущено, по умолчанию используется true.
    /// </summary>
    [JsonPropertyName("shrinkToFit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsShrinkToFit { get; set; }

    /// <summary>
    /// Коллекция диапазонов страниц для печати в результирующем выводе. Объекты списка должны быть строками или целыми числами. Другие типы значений вызовут ошибку при отправке команды browsingContext.print.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<object> PageRanges { get; set; } = [];
}