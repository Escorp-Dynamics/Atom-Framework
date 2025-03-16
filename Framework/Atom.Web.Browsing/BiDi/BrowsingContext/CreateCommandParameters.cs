using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.create.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CreateCommandParameters"/>.
/// </remarks>
/// <param name="createType">Тип контекста просмотра для создания.</param>
public class CreateCommandParameters(CreateType createType) : CommandParameters<CreateCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.create";

    /// <summary>
    /// Тип контекста просмотра (вкладка или окно) для создания.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("type")]
    public CreateType CreateType { get; set; } = createType;

    /// <summary>
    /// Идентификатор контекста просмотра, на который будет ссылаться вновь созданный контекст.
    /// </summary>
    [JsonPropertyName("referenceContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReferenceContextId { get; set; }

    /// <summary>
    /// Указывает, следует ли создавать новый контекст просмотра в фоновом режиме.
    /// </summary>
    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsCreatedInBackground { get; set; }

    /// <summary>
    /// Идентификатор пользовательского контекста, в котором будет создан новый контекст просмотра.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserContextId { get; set; }
}