namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// The Session module contains commands and events for monitoring the status of the remote end.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SessionModule"/> class.
/// </remarks>
/// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
public sealed class SessionModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// The name of the session module.
    /// </summary>
    public const string SessionModuleName = "session";

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => SessionModuleName;

    /// <summary>
    /// Gets the status of the current connection.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the information about the remote end status.</returns>
    public ValueTask<StatusCommandResult> StatusAsync(StatusCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.StatusCommandParameters, JsonContext.Default.CommandResponseMessageStatusCommandResult);

    /// <summary>
    /// Creates a new session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the information new session.</returns>
    public ValueTask<NewCommandResult> NewSessionAsync(NewCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.NewCommandParameters, JsonContext.Default.CommandResponseMessageNewCommandResult);

    /// <summary>
    /// Subscribes to events for this session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing the subscription ID.</returns>
    public ValueTask<SubscribeCommandResult> SubscribeAsync(SubscribeCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SubscribeCommandParameters, JsonContext.Default.CommandResponseMessageSubscribeCommandResult);

    /// <summary>
    /// Unsubscribes from events for this session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>An empty command result.</returns>
    public ValueTask<EmptyResult> UnsubscribeAsync(UnsubscribeCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.UnsubscribeCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Ends the current session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>An empty command result.</returns>
    public ValueTask<EmptyResult> EndAsync(EndCommandParameters? commandProperties = null) => Driver.ExecuteCommandAsync(commandProperties ?? new(), JsonContext.Default.EndCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}