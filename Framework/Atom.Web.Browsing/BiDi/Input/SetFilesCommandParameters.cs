using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет параметры для команды input.setFiles.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SetFilesCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, содержащего элемент, для которого нужно установить файлы.</param>
/// <param name="element">Элемент, для которого нужно установить список файлов. Должен быть типа {input type="file"}.</param>
public class SetFilesCommandParameters(string browsingContextId, SharedReference element) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Название метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "input.setFiles";

    /// <summary>
    /// Идентификатор контекста просмотра, содержащего элемент, для которого нужно установить файлы.
    /// </summary>
    [JsonPropertyName("context")]
    public string ContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Элемент, для которого нужно установить список файлов. Элемент должен быть типа {input type="file"}.
    /// </summary>
    public SharedReference Element { get; set; } = element;

    /// <summary>
    /// Коллекция файлов, которые будут установлены для элемента.
    /// </summary>
    public IEnumerable<string> Files { get; set; } = [];
}