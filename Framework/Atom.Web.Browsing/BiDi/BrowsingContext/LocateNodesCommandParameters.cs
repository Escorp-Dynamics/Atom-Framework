using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.locateNodes.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="LocateNodesCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, в котором нужно найти узлы.</param>
/// <param name="locator">Локатор, используемый для поиска узлов.</param>
public class LocateNodesCommandParameters(string browsingContextId, Locator locator) : CommandParameters<LocateNodesCommandResult>
{
    [JsonPropertyName("contextNodes")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal IList<SharedReference>? SerializableContextNodes => ContextNodes.Any() ? [.. ContextNodes] : default;

    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.locateNodes";

    /// <summary>
    /// Идентификатор контекста просмотра, в котором нужно найти узлы.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Локатор, используемый для поиска узлов.
    /// </summary>
    [JsonRequired]
    public Locator Locator { get; set; } = locator;

    /// <summary>
    /// Максимальное количество узлов, возвращаемых командой. Если опущено или <see langword="null"/>, команда возвращает все найденные узлы.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? MaxNodeCount { get; set; }

    /// <summary>
    /// Параметры сериализации для сериализации ссылок на найденные узлы.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SerializationOptions? SerializationOptions { get; set; }

    /// <summary>
    /// Коллекция контекстных узлов, внутри которых нужно найти дочерние узлы. Если пусто, узлы будут найдены начиная с верхнего уровня документа.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<SharedReference> ContextNodes { get; set; } = [];
}