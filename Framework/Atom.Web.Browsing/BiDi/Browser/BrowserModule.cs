namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет модуль Browser, который содержит команды для управления процессом браузера на удалённой стороне.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="BrowserModule"/>.
/// </remarks>
/// <param name="driver">Объект <see cref="BiDiDriver"/>, используемый в командах и событиях модуля.</param>
public sealed class BrowserModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// Имя модуля browser.
    /// </summary>
    public const string BrowserModuleName = "browser";

    /// <summary>
    /// Имя модуля.
    /// </summary>
    public override string ModuleName => BrowserModuleName;

    /// <summary>
    /// Завершает все сессии WebDriver и очищает состояние автоматизации в удалённом экземпляре браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> CloseAsync(CloseCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.BrowserCloseCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Создаёт новый пользовательский контекст для браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Объект, описывающий информацию о созданном пользовательском контексте.</returns>
    public ValueTask<CreateUserContextCommandResult> CreateUserContextAsync(CreateUserContextCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.CreateUserContextCommandParameters, JsonContext.Default.CommandResponseMessageCreateUserContextCommandResult);

    /// <summary>
    /// Получает список информации о клиентских окнах для текущего браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Доступный только для чтения список клиентских окон, открытых в этом браузере.</returns>
    public ValueTask<GetClientWindowsCommandResult> GetClientWindowsAsync(GetClientWindowsCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.GetClientWindowsCommandParameters, JsonContext.Default.CommandResponseMessageGetClientWindowsCommandResult);

    /// <summary>
    /// Получает список открытых пользовательских контекстов для браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Доступный только для чтения список пользовательских контекстов, открытых в этом браузере.</returns>
    public ValueTask<GetUserContextsCommandResult> GetUserContextsAsync(GetUserContextsCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.GetUserContextsCommandParameters, JsonContext.Default.CommandResponseMessageGetUserContextsCommandResult);

    /// <summary>
    /// Удаляет пользовательский контекст для браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат выполнения команды.</returns>
    public ValueTask<EmptyResult> RemoveUserContextAsync(RemoveUserContextCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.RemoveUserContextCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Устанавливает состояние клиентского окна, включая размер и положение. Обратите внимание, что удалённая сторона может не поддерживать установку окна в запрошенное состояние, и это не обязательно приведёт к ошибке.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Фактическое состояние окна после установки состояния.</returns>
    public ValueTask<SetClientWindowStateCommandResult> SetClientWindowStateAsync(SetClientWindowStateCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetClientWindowStateCommandParameters, JsonContext.Default.CommandResponseMessageSetClientWindowStateCommandResult);
}