namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет модуль BrowsingContext, который содержит команды и события, связанные с контекстами просмотра.
/// </summary>
public sealed class BrowsingContextModule : Module
{
    /// <summary>
    /// Имя модуля browsingContext.
    /// </summary>
    public const string BrowsingContextModuleName = "browsingContext";

    /// <summary>
    /// Имя модуля.
    /// </summary>
    public override string ModuleName => BrowsingContextModuleName;

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о создании контекста просмотра.
    /// </summary>
    public ObservableEvent<BrowsingContextEventArgs> OnContextCreated { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет об уничтожении контекста просмотра.
    /// </summary>
    public ObservableEvent<BrowsingContextEventArgs> OnContextDestroyed { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о начале навигации в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnNavigationStarted { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о навигации по фрагменту в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnFragmentNavigated { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о загрузке DOM-контента в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnDomContentLoaded { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о начале загрузки в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnDownloadWillBegin { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о загрузке контента в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnLoad { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о прерывании навигации в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnNavigationAborted { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о сбое навигации в контексте просмотра.
    /// </summary>
    public ObservableEvent<NavigationEventArgs> OnNavigationFailed { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет об обновлении истории браузера.
    /// </summary>
    public ObservableEvent<HistoryUpdatedEventArgs> OnHistoryUpdated { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет об открытии пользовательского запроса.
    /// </summary>
    public ObservableEvent<UserPromptOpenedEventArgs> OnUserPromptOpened { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о закрытии пользовательского запроса.
    /// </summary>
    public ObservableEvent<UserPromptClosedEventArgs> OnUserPromptClosed { get; } = new();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BrowsingContextModule"/>.
    /// </summary>
    /// <param name="driver">Объект <see cref="BiDiDriver"/>, используемый в командах и событиях модуля.</param>
    public BrowsingContextModule(BiDiDriver driver) : base(driver)
    {
        RegisterAsyncEventInvoker("browsingContext.contextCreated", JsonContext.Default.EventMessageBrowsingContextInfo, OnContextCreatedAsync);
        RegisterAsyncEventInvoker("browsingContext.contextDestroyed", JsonContext.Default.EventMessageBrowsingContextInfo, OnContextDestroyedAsync);
        RegisterAsyncEventInvoker("browsingContext.navigationStarted", JsonContext.Default.EventMessageNavigationEventArgs, OnNavigationStartedAsync);
        RegisterAsyncEventInvoker("browsingContext.fragmentNavigated", JsonContext.Default.EventMessageNavigationEventArgs, OnFragmentNavigatedAsync);
        RegisterAsyncEventInvoker("browsingContext.domContentLoaded", JsonContext.Default.EventMessageNavigationEventArgs, OnDomContentLoadedAsync);
        RegisterAsyncEventInvoker("browsingContext.load", JsonContext.Default.EventMessageNavigationEventArgs, OnLoadAsync);
        RegisterAsyncEventInvoker("browsingContext.downloadWillBegin", JsonContext.Default.EventMessageNavigationEventArgs, OnDownloadWillBeginAsync);
        RegisterAsyncEventInvoker("browsingContext.navigationAborted", JsonContext.Default.EventMessageNavigationEventArgs, OnNavigationAbortedAsync);
        RegisterAsyncEventInvoker("browsingContext.navigationFailed", JsonContext.Default.EventMessageNavigationEventArgs, OnNavigationFailedAsync);
        RegisterAsyncEventInvoker("browsingContext.historyUpdated", JsonContext.Default.EventMessageHistoryUpdatedEventArgs, OnHistoryUpdatedAsync);
        RegisterAsyncEventInvoker("browsingContext.userPromptClosed", JsonContext.Default.EventMessageUserPromptClosedEventArgs, OnUserPromptClosedAsync);
        RegisterAsyncEventInvoker("browsingContext.userPromptOpened", JsonContext.Default.EventMessageUserPromptOpenedEventArgs, OnUserPromptOpenedAsync);
    }

    private async ValueTask OnContextCreatedAsync(EventInfo<BrowsingContextInfo> eventData)
    {
        var eventArgs = eventData.ToEventArgs<BrowsingContextEventArgs>();
        await OnContextCreated.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnContextDestroyedAsync(EventInfo<BrowsingContextInfo> eventData)
    {
        var eventArgs = eventData.ToEventArgs<BrowsingContextEventArgs>();
        await OnContextDestroyed.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnNavigationStartedAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnNavigationStarted.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnFragmentNavigatedAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnFragmentNavigated.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnDomContentLoadedAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnDomContentLoaded.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnLoadAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnLoad.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnDownloadWillBeginAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnDownloadWillBegin.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnNavigationAbortedAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnNavigationAborted.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnNavigationFailedAsync(EventInfo<NavigationEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<NavigationEventArgs>();
        await OnNavigationFailed.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnHistoryUpdatedAsync(EventInfo<HistoryUpdatedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<HistoryUpdatedEventArgs>();
        await OnHistoryUpdated.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnUserPromptClosedAsync(EventInfo<UserPromptClosedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<UserPromptClosedEventArgs>();
        await OnUserPromptClosed.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnUserPromptOpenedAsync(EventInfo<UserPromptOpenedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<UserPromptOpenedEventArgs>();
        await OnUserPromptOpened.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Активирует контекст просмотра, выводя его на передний план.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды, содержащий скриншот в формате base64.</returns>
    public ValueTask<EmptyResult> ActivateAsync(ActivateCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ActivateCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Создаёт скриншот текущей страницы в контексте просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды, содержащий скриншот в формате base64.</returns>
    public ValueTask<CaptureScreenshotCommandResult> CaptureScreenshotAsync(CaptureScreenshotCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.CaptureScreenshotCommandParameters, JsonContext.Default.CommandResponseMessageCaptureScreenshotCommandResult);

    /// <summary>
    /// Закрывает контекст просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> CloseAsync(CloseCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.BrowsingContextCloseCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Создаёт новый контекст просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды, включая идентификатор нового контекста.</returns>
    public ValueTask<CreateCommandResult> CreateAsync(CreateCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.CreateCommandParameters, JsonContext.Default.CommandResponseMessageCreateCommandResult);

    /// <summary>
    /// Получает дерево контекстов просмотра, связанных с указанным контекстом.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Дерево связанных контекстов просмотра.</returns>
    public ValueTask<GetTreeCommandResult> GetTreeAsync(GetTreeCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.GetTreeCommandParameters, JsonContext.Default.CommandResponseMessageGetTreeCommandResult);

    /// <summary>
    /// Обрабатывает пользовательский запрос.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> HandleUserPromptAsync(HandleUserPromptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.HandleUserPromptCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Находит узлы в контексте просмотра.
    /// </summary>
    /// <param name="commandParameters">Параметры команды.</param>
    /// <returns>Результат команды, содержащий найденные узлы, если они есть.</returns>
    public ValueTask<LocateNodesCommandResult> LocateNodesAsync(LocateNodesCommandParameters commandParameters) => Driver.ExecuteCommandAsync(commandParameters, JsonContext.Default.LocateNodesCommandParameters, JsonContext.Default.CommandResponseMessageLocateNodesCommandResult);

    /// <summary>
    /// Переходит по новому URL в контексте просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды.</returns>
    public ValueTask<NavigationResult> NavigateAsync(NavigateCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.NavigateCommandParameters, JsonContext.Default.CommandResponseMessageNavigationResult);

    /// <summary>
    /// Печатает PDF текущей страницы в контексте просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды, содержащий PDF в формате base64.</returns>
    public ValueTask<PrintCommandResult> PrintAsync(PrintCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.PrintCommandParameters, JsonContext.Default.CommandResponseMessagePrintCommandResult);

    /// <summary>
    /// Перезагружает контекст просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды.</returns>
    public ValueTask<NavigationResult> ReloadAsync(ReloadCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ReloadCommandParameters, JsonContext.Default.CommandResponseMessageNavigationResult);

    /// <summary>
    /// Устанавливает размеры области просмотра для контекста просмотра.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды.</returns>
    public ValueTask<EmptyResult> SetViewportAsync(SetViewportCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetViewportCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Перемещается по записям истории браузера.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Результат команды.</returns>
    public ValueTask<EmptyResult> TraverseHistoryAsync(TraverseHistoryCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.TraverseHistoryCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}