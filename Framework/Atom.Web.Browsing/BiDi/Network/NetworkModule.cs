namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The Network module contains commands and events relating to network traffic.
/// </summary>
public sealed class NetworkModule : Module
{
    /// <summary>
    /// The name of the log module.
    /// </summary>
    public const string NetworkModuleName = "network";

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkModule"/> class.
    /// </summary>
    /// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
    public NetworkModule(BiDiDriver driver) : base(driver)
    {
        RegisterAsyncEventInvoker("network.authRequired", JsonContext.Default.EventMessageAuthRequiredEventArgs, OnAuthRequiredAsync);
        RegisterAsyncEventInvoker("network.beforeRequestSent", JsonContext.Default.EventMessageBeforeRequestSentEventArgs, OnBeforeRequestSentAsync);
        RegisterAsyncEventInvoker("network.fetchError", JsonContext.Default.EventMessageFetchErrorEventArgs, OnFetchErrorAsync);
        RegisterAsyncEventInvoker("network.responseStarted", JsonContext.Default.EventMessageResponseStartedEventArgs, OnResponseStartedAsync);
        RegisterAsyncEventInvoker("network.responseCompleted", JsonContext.Default.EventMessageResponseCompletedEventArgs, OnResponseCompletedAsync);
    }

    /// <summary>
    /// Gets an observable event that notifies when an authorization required response is received.
    /// </summary>
    public ObservableEvent<AuthRequiredEventArgs> OnAuthRequired { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies before a network request is sent.
    /// </summary>
    public ObservableEvent<BeforeRequestSentEventArgs> OnBeforeRequestSent { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when an error is encountered fetching data.
    /// </summary>
    public ObservableEvent<FetchErrorEventArgs> OnFetchError { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when network response has started.
    /// </summary>
    public ObservableEvent<ResponseStartedEventArgs> OnResponseStarted { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when network response has completed.
    /// </summary>
    public ObservableEvent<ResponseCompletedEventArgs> OnResponseCompleted { get; } = new();

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => NetworkModuleName;

    /// <summary>
    /// Adds an intercept for network traffic matching specific phases of the traffic and URL patterns.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing a network interception ID.</returns>
    public ValueTask<AddInterceptCommandResult> AddInterceptAsync(AddInterceptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.AddInterceptCommandParameters, JsonContext.Default.CommandResponseMessageAddInterceptCommandResult);

    /// <summary>
    /// Continues a paused request intercepted by the driver.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> ContinueRequestAsync(ContinueRequestCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ContinueRequestCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Continues a paused response intercepted by the driver after the response has been received from the server,
    /// but before presented to the browser.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> ContinueResponseAsync(ContinueResponseCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ContinueResponseCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Continues a paused request intercepted by the driver with authentication information.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> ContinueWithAuthAsync(ContinueWithAuthCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ContinueWithAuthCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Fails a paused request intercepted by the driver.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> FailRequestAsync(FailRequestCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.FailRequestCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Provides a full response for request intercepted by the driver without sending the request to the server.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> ProvideResponseAsync(ProvideResponseCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ProvideResponseCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Removes an added intercept for network traffic matching specific phases of the traffic and URL patterns.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> RemoveInterceptAsync(RemoveInterceptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.RemoveInterceptCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Sets the cache behavior of the browser.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<EmptyResult> SetCacheBehaviorAsync(SetCacheBehaviorCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetCacheBehaviorCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    private async ValueTask OnAuthRequiredAsync(EventInfo<AuthRequiredEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<AuthRequiredEventArgs>();
        await OnAuthRequired.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnBeforeRequestSentAsync(EventInfo<BeforeRequestSentEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<BeforeRequestSentEventArgs>();
        await OnBeforeRequestSent.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnFetchErrorAsync(EventInfo<FetchErrorEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<FetchErrorEventArgs>();
        await OnFetchError.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnResponseStartedAsync(EventInfo<ResponseStartedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<ResponseStartedEventArgs>();
        await OnResponseStarted.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnResponseCompletedAsync(EventInfo<ResponseCompletedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<ResponseCompletedEventArgs>();
        await OnResponseCompleted.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }
}
