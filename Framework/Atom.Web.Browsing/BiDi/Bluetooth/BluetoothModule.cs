namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет модуль Bluetooth, который содержит команды для симуляции взаимодействия с Bluetooth-устройствами.
/// </summary>
public sealed class BluetoothModule : Module
{
    /// <summary>
    /// Имя модуля Bluetooth.
    /// </summary>
    public const string BluetoothModuleName = "bluetooth";

    /// <summary>
    /// Имя модуля.
    /// </summary>
    public override string ModuleName => BluetoothModuleName;

    /// <summary>
    /// Наблюдаемое событие, которое уведомляет об обновлении запроса на подключение к Bluetooth-устройству.
    /// </summary>
    public ObservableEvent<RequestDevicePromptUpdatedEventArgs> OnRequestDevicePromptUpdated { get; } = new();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BluetoothModule"/>.
    /// </summary>
    /// <param name="driver">Объект <see cref="BiDiDriver"/>, используемый в командах и событиях модуля.</param>
    public BluetoothModule(BiDiDriver driver) : base(driver) => RegisterAsyncEventInvoker("bluetooth.requestDevicePromptUpdated", JsonContext.Default.EventMessageRequestDevicePromptUpdatedEventArgs, OnRequestDevicePromptUpdatedAsync);

    private async ValueTask OnRequestDevicePromptUpdatedAsync(EventInfo<RequestDevicePromptUpdatedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<RequestDevicePromptUpdatedEventArgs>();
        await OnRequestDevicePromptUpdated.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Обрабатывает запрос на подключение к Bluetooth-устройствам.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> HandleRequestDevicePromptAsync(HandleRequestDevicePromptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.HandleRequestDevicePromptCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Симулирует наличие или отсутствие Bluetooth-адаптера, а также его настройки питания.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> SimulateAdapterAsync(SimulateAdapterCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SimulateAdapterCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Симулирует рекламу доступности Bluetooth-периферийных устройств.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> SimulateAdvertisementAsync(SimulateAdvertisementCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SimulateAdvertisementCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Симулирует наличие Bluetooth-периферийного устройства, уже подключённого к странице.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> SimulatePreConnectedPeripheralAsync(SimulatePreConnectedPeripheralCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SimulatePreConnectedPeripheralCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}