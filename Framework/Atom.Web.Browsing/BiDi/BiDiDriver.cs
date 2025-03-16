using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Atom.Web.Browsing.BiDi.Bluetooth;
using Atom.Web.Browsing.BiDi.Browser;
using Atom.Web.Browsing.BiDi.BrowsingContext;
using Atom.Web.Browsing.BiDi.Input;
using Atom.Web.Browsing.BiDi.Log;
using Atom.Web.Browsing.BiDi.Network;
using Atom.Web.Browsing.BiDi.Permissions;
using Atom.Web.Browsing.BiDi.Protocol;
using Atom.Web.Browsing.BiDi.Script;
using Atom.Web.Browsing.BiDi.Session;
using Atom.Web.Browsing.BiDi.Storage;
using Atom.Web.Browsing.BiDi.WebExtension;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет объект, содержащий команды для управления браузером с использованием протокола WebDriver Bidi.
/// </summary>
public class BiDiDriver : IDisposable
{
    private readonly TimeSpan defaultCommandWaitTimeout;
    private readonly Transport transport;
    private readonly Dictionary<string, Module> modules = [];
    private bool disposedValue;

    /// <summary>
    /// Определяет, установлено ли подключение к браузеру.
    /// </summary>
    public bool IsConnected => transport.IsConnected;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BiDiDriver"/>.
    /// </summary>
    public BiDiDriver() : this(Timeout.InfiniteTimeSpan, new Transport()) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BiDiDriver"/> с указанным временем ожидания выполнения команды по умолчанию.
    /// </summary>
    /// <param name="defaultCommandWaitTimeout">Время ожидания выполнения команды по умолчанию.</param>
    public BiDiDriver(TimeSpan defaultCommandWaitTimeout) : this(defaultCommandWaitTimeout, new Transport()) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="BiDiDriver"/> с указанным временем ожидания выполнения команды по умолчанию и объектом <see cref="Transport"/>.
    /// </summary>
    /// <param name="defaultCommandWaitTimeout">Время ожидания выполнения команды по умолчанию.</param>
    /// <param name="transport">Объект транспорта протокола, используемый для связи с браузером.</param>
    public BiDiDriver(TimeSpan defaultCommandWaitTimeout, Transport transport)
    {
        this.defaultCommandWaitTimeout = defaultCommandWaitTimeout;
        this.transport = transport;
        this.transport.OnEventReceived.AddObserver(OnTransportEventReceivedAsync);
        this.transport.OnErrorEventReceived.AddObserver(OnTransportErrorEventReceivedAsync);
        this.transport.OnUnknownMessageReceived.AddObserver(OnTransportUnknownMessageReceivedAsync);
        this.transport.OnLogMessage.AddObserver(OnTransportLogMessageAsync);

        RegisterModules();
    }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="BiDiDriver"/> с указанным временем ожидания выполнения команды по умолчанию и объектом <see cref="Transport"/>.
    /// </summary>
    /// <param name="transport">Объект транспорта протокола, используемый для связи с браузером.</param>
    public BiDiDriver(Transport transport) : this(Timeout.InfiniteTimeSpan, transport) { }

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о получении события протокола от транспорта протокола.
    /// </summary>
    public ObservableEvent<EventReceivedEventArgs> OnEventReceived { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о получении ошибки протокола от транспорта протокола.
    /// </summary>
    public ObservableEvent<ErrorReceivedEventArgs> OnUnexpectedErrorReceived { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о получении неизвестного сообщения от транспорта протокола.
    /// </summary>
    public ObservableEvent<UnknownMessageReceivedEventArgs> OnUnknownMessageReceived { get; } = new();

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет о получении сообщения журнала, созданного этим драйвером.
    /// </summary>
    public ObservableEvent<LogMessageEventArgs> OnLogMessage { get; } = new();

    /// <summary>
    /// Модуль Bluetooth, описанный в спецификации W3C Web Bluetooth.
    /// </summary>
    public BluetoothModule Bluetooth => GetModule<BluetoothModule>(BluetoothModule.BluetoothModuleName);

    /// <summary>
    /// Модуль Browser, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public BrowserModule Browser => GetModule<BrowserModule>(BrowserModule.BrowserModuleName);

    /// <summary>
    /// Модуль BrowsingContext, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public BrowsingContextModule BrowsingContext => GetModule<BrowsingContextModule>(BrowsingContextModule.BrowsingContextModuleName);

    /// <summary>
    /// Модуль Session, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public SessionModule Session => GetModule<SessionModule>(SessionModule.SessionModuleName);

    /// <summary>
    /// Модуль Script, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public ScriptModule Script => GetModule<ScriptModule>(ScriptModule.ScriptModuleName);

    /// <summary>
    /// Модуль Log, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public LogModule Log => GetModule<LogModule>(LogModule.LogModuleName);

    /// <summary>
    /// Модуль Input, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public InputModule Input => GetModule<InputModule>(InputModule.InputModuleName);

    /// <summary>
    /// Модуль Network, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public NetworkModule Network => GetModule<NetworkModule>(NetworkModule.NetworkModuleName);

    /// <summary>
    /// Модуль Permissions, описанный в спецификации W3C Permissions.
    /// </summary>
    public PermissionsModule Permissions => GetModule<PermissionsModule>(PermissionsModule.PermissionsModuleName);

    /// <summary>
    /// Модуль Storage, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public StorageModule Storage => GetModule<StorageModule>(StorageModule.StorageModuleName);

    /// <summary>
    /// Модуль WebExtension, описанный в протоколе WebDriver Bidi.
    /// </summary>
    public WebExtensionModule WebExtension => GetModule<WebExtensionModule>(WebExtensionModule.WebExtensionModuleName);

    private void RegisterModules()
    {
        RegisterModule(new BluetoothModule(this));
        RegisterModule(new BrowserModule(this));
        RegisterModule(new BrowsingContextModule(this));
        RegisterModule(new LogModule(this));
        RegisterModule(new InputModule(this));
        RegisterModule(new NetworkModule(this));
        RegisterModule(new PermissionsModule(this));
        RegisterModule(new SessionModule(this));
        RegisterModule(new ScriptModule(this));
        RegisterModule(new StorageModule(this));
        RegisterModule(new WebExtensionModule(this));
    }

    private ValueTask OnTransportEventReceivedAsync(EventReceivedEventArgs e) => OnEventReceived.NotifyObserversAsync(e);

    private ValueTask OnTransportErrorEventReceivedAsync(ErrorReceivedEventArgs e) => OnUnexpectedErrorReceived.NotifyObserversAsync(e);

    private ValueTask OnTransportUnknownMessageReceivedAsync(UnknownMessageReceivedEventArgs e) => OnUnknownMessageReceived.NotifyObserversAsync(e);

    private ValueTask OnTransportLogMessageAsync(LogMessageEventArgs e) => OnLogMessage.NotifyObserversAsync(e);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue) return;
        disposedValue = true;

        if (!disposing) return;
        transport.Dispose();
    }

    /// <summary>
    /// Начинает связь с удалённой стороной протокола WebDriver Bidi.
    /// </summary>
    /// <param name="url">URL удалённой стороны.</param>
    /// <returns>Объект задачи, представляющий асинхронную операцию.</returns>
    public virtual ValueTask StartAsync(Uri url) => transport.ConnectAsync(url);

    /// <summary>
    /// Останавливает связь с удалённой стороной протокола WebDriver Bidi.
    /// </summary>
    /// <returns>Объект задачи, представляющий асинхронную операцию.</returns>
    public virtual ValueTask StopAsync() => transport.DisconnectAsync();

    /// <summary>
    /// Отправляет команду удалённой стороне протокола WebDriver Bidi и ожидает завершения
    /// в течение времени ожидания по умолчанию.
    /// </summary>
    /// <typeparam name="TParams">Ожидаемый тип параметров команды.</typeparam>
    /// <typeparam name="TResult">Ожидаемый тип результата команды.</typeparam>
    /// <param name="command">Объект, содержащий настройки команды, включая параметры.</param>
    /// <param name="parametersTypeInfo">Информация о типе параметров результата.</param>
    /// <param name="resultTypeInfo">Информация о типе результата.</param>
    /// <returns>Объект задачи, представляющий асинхронную операцию.</returns>
    /// <exception cref="BiDiException">Выбрасывается, если возникает ошибка при выполнении команды.</exception>
    public virtual ValueTask<TResult> ExecuteCommandAsync<TParams, TResult>(TParams command, JsonTypeInfo<TParams> parametersTypeInfo, JsonTypeInfo<CommandResponseMessage<TResult>> resultTypeInfo)
        where TParams : CommandParameters<TResult>
        where TResult : CommandResult
        => ExecuteCommandAsync(command, parametersTypeInfo, resultTypeInfo, defaultCommandWaitTimeout);

    /// <summary>
    /// Отправляет команду удалённой стороне протокола WebDriver Bidi и ожидает ответа.
    /// </summary>
    /// <typeparam name="TParams">Ожидаемый тип параметров команды.</typeparam>
    /// <typeparam name="TResult">Ожидаемый тип результата команды.</typeparam>
    /// <param name="command">Объект, содержащий настройки команды, включая параметры.</param>
    /// <param name="parametersTypeInfo">Информация о типе параметров результата.</param>
    /// <param name="resultTypeInfo">Информация о типе результата.</param>
    /// <param name="commandTimeout">Время ожидания завершения команды.</param>
    /// <returns>Объект задачи, представляющий асинхронную операцию.</returns>
    /// <exception cref="BiDiException">Выбрасывается, если возникает ошибка при выполнении команды.</exception>
    public virtual async ValueTask<TResult> ExecuteCommandAsync<TParams, TResult>([NotNull] TParams command, JsonTypeInfo<TParams> parametersTypeInfo, JsonTypeInfo<CommandResponseMessage<TResult>> resultTypeInfo, TimeSpan commandTimeout)
        where TParams : CommandParameters<TResult>
        where TResult : CommandResult
    {
        var sentCommand = await transport.SendCommandAsync(command, parametersTypeInfo, resultTypeInfo).ConfigureAwait(false);
        var commandCompleted = await sentCommand.WaitForCompletionAsync(commandTimeout).ConfigureAwait(false);

        if (!commandCompleted) throw new BiDiException($"Превышено время ожидания выполнения команды {command.MethodName} после {commandTimeout.TotalMilliseconds} миллисекунд");

        if (sentCommand.Result is null)
        {
            if (sentCommand.ThrownException is null)
                throw new BiDiException($"Результат и исключение для команды {command.MethodName} с id {sentCommand.CommandId} равны null");

            throw sentCommand.ThrownException;
        }

        var result = sentCommand.Result;

        if (result.IsError)
        {
            if (result is not ErrorResult errorResponse) throw new BiDiException("Не удалось преобразовать ответ об ошибке от транспорта для SendCommandAndWait в ErrorResult");
            throw new BiDiException($"Получена ошибка '{errorResponse.ErrorType}' при выполнении команды {command.MethodName}: {errorResponse.ErrorMessage}");
        }

        return result is not TResult convertedResult
            ? throw new BiDiException($"Не удалось преобразовать ответ от транспорта для SendCommandAndWait в {typeof(TResult)}")
            : convertedResult;
    }

    /// <summary>
    /// Регистрирует модуль для использования с этим драйвером.
    /// </summary>
    /// <param name="module">Объект модуля.</param>
    public virtual void RegisterModule([NotNull] Module module) => modules[module.ModuleName] = module;

    /// <summary>
    /// Получает модуль из набора зарегистрированных модулей для этого драйвера.
    /// </summary>
    /// <typeparam name="T">Объект модуля, который является подклассом <see cref="Module"/>.</typeparam>
    /// <param name="moduleName">Имя модуля для возврата.</param>
    /// <returns>Объект модуля протокола.</returns>
    public virtual T GetModule<T>(string moduleName) where T : Module => !modules.TryGetValue(moduleName, out var module)
        ? throw new BiDiException($"Модуль '{moduleName}' не зарегистрирован в этом драйвере") : module is not T
        ? throw new BiDiException($"Модуль '{moduleName}' зарегистрирован в этом драйвере, но объект модуля не является типом {typeof(T)}") : (T)module;

    /// <summary>
    /// Регистрирует событие, которое будет вызвано удалённой стороной протокола WebDriver Bidi.
    /// </summary>
    /// <typeparam name="T">Тип данных, которые будут переданы событием.</typeparam>
    /// <param name="eventName">Имя события для вызова.</param>
    /// <param name="typeInfo">Информация о типе.</param>
    public virtual void RegisterEvent<T>(string eventName, JsonTypeInfo<EventMessage<T>> typeInfo) => transport.RegisterEventMessage(eventName, typeInfo);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}