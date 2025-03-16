namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// The Script module contains commands and events relating to script realms and execution.
/// </summary>
public sealed class ScriptModule : Module
{
    /// <summary>
    /// The name of the script module.
    /// </summary>
    public const string ScriptModuleName = "script";

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptModule"/> class.
    /// </summary>
    /// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
    public ScriptModule(BiDiDriver driver) : base(driver)
    {
        RegisterAsyncEventInvoker("script.realmCreated", JsonContext.Default.EventMessageRealmInfo, OnRealmCreatedAsync);
        RegisterAsyncEventInvoker("script.realmDestroyed", JsonContext.Default.EventMessageRealmDestroyedEventArgs, OnRealmDestroyedAsync);
        RegisterAsyncEventInvoker("script.message", JsonContext.Default.EventMessageMessageEventArgs, OnMessageAsync);
    }

    /// <summary>
    /// Gets an observable event that notifies when a new script realm is created.
    /// </summary>
    public ObservableEvent<RealmCreatedEventArgs> OnRealmCreated { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies with a script realm is destroyed.
    /// </summary>
    public ObservableEvent<RealmDestroyedEventArgs> OnRealmDestroyed { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when a preload script sends data to the client.
    /// </summary>
    public ObservableEvent<MessageEventArgs> OnMessage { get; } = new();

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => ScriptModuleName;

    /// <summary>
    /// Adds a preload script to each page before execution of other JavaScript included in the page source.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the ID of the created preload script.</returns>
    public ValueTask<AddPreloadScriptCommandResult> AddPreloadScriptAsync(AddPreloadScriptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.AddPreloadScriptCommandParameters, JsonContext.Default.CommandResponseMessageAddPreloadScriptCommandResult);

    /// <summary>
    /// Calls a function in the specified script target.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the result of the function execution.</returns>
    public ValueTask<EvaluateResult> CallFunctionAsync(CallFunctionCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.CallFunctionCommandParameters, JsonContext.Default.CommandResponseMessageEvaluateResult);

    /// <summary>
    /// Disowns the specified handles to allow the script engine to garbage collect objects.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>An empty command result.</returns>
    public ValueTask<EmptyResult> DisownAsync(DisownCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.DisownCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Evaluates a piece of JavaScript in the specified script target.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the result of the script evaluation.</returns>
    public ValueTask<EvaluateResult> EvaluateAsync(EvaluateCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.EvaluateCommandParameters, JsonContext.Default.CommandResponseMessageEvaluateResult);

    /// <summary>
    /// Gets the realms associated with a given browsing context and realm type.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing IDs of the realms.</returns>
    public ValueTask<GetRealmsCommandResult> GetRealmsAsync(GetRealmsCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.GetRealmsCommandParameters, JsonContext.Default.CommandResponseMessageGetRealmsCommandResult);

    /// <summary>
    /// Removes a preload script from loading on each page before execution of other JavaScript included in the page source.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>An empty command result.</returns>
    public ValueTask<EmptyResult> RemovePreloadScriptAsync(RemovePreloadScriptCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.RemovePreloadScriptCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    private async ValueTask OnRealmCreatedAsync(EventInfo<RealmInfo> eventData)
    {
        var eventArgs = eventData.ToEventArgs<RealmCreatedEventArgs>();
        await OnRealmCreated.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnRealmDestroyedAsync(EventInfo<RealmDestroyedEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<RealmDestroyedEventArgs>();
        await OnRealmDestroyed.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }

    private async ValueTask OnMessageAsync(EventInfo<MessageEventArgs> eventData)
    {
        var eventArgs = eventData.ToEventArgs<MessageEventArgs>();
        await OnMessage.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }
}
