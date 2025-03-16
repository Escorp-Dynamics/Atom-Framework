namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Предоставляет параметры для команды bluetooth.handleRequestDevicePrompt, чтобы отменить запрос на подключение.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HandleRequestDevicePromptCancelCommandParameters"/> для отмены запроса на устройство.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого отменяется запрос.</param>
/// <param name="promptId">Идентификатор запроса, который нужно отменить.</param>
public class HandleRequestDevicePromptCancelCommandParameters(string browsingContextId, string promptId) : HandleRequestDevicePromptCommandParameters(browsingContextId, promptId, default);