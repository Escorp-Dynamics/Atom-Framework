namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// The Storage module contains commands and events relating to browser storage such as cookies.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StorageModule"/> class.
/// </remarks>
/// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
public sealed class StorageModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// The name of the browsingContext module.
    /// </summary>
    public const string StorageModuleName = "storage";

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => StorageModuleName;

    /// <summary>
    /// Gets cookies from the browser session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<GetCookiesCommandResult> GetCookiesAsync(GetCookiesCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.GetCookiesCommandParameters, JsonContext.Default.CommandResponseMessageGetCookiesCommandResult);

    /// <summary>
    /// Sets a cookie in the browser session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<SetCookieCommandResult> SetCookieAsync(SetCookieCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetCookieCommandParameters, JsonContext.Default.CommandResponseMessageSetCookieCommandResult);

    /// <summary>
    /// Deletes cookies from the browser session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command.</returns>
    public ValueTask<DeleteCookiesCommandResult> DeleteCookiesAsync(DeleteCookiesCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.DeleteCookiesCommandParameters, JsonContext.Default.CommandResponseMessageDeleteCookiesCommandResult);
}