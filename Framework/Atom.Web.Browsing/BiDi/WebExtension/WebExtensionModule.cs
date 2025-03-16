namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// The WebExtension module contains commands and events relating to web extensions in the browser.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="WebExtensionModule"/> class.
/// </remarks>
/// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
public sealed class WebExtensionModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// The name of the webExtension module.
    /// </summary>
    public const string WebExtensionModuleName = "webExtension";

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => WebExtensionModuleName;

    /// <summary>
    /// Installs a web extension into the current driver session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>A Task containing the result of the command including the ID of the installed extension.</returns>
    public ValueTask<InstallCommandResult> InstallAsync(InstallCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.InstallCommandParameters, JsonContext.Default.CommandResponseMessageInstallCommandResult);

    /// <summary>
    /// Uninstalls a web extension from the current driver session.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>A Task containing the result of the asynchronous operation.</returns>
    public ValueTask<EmptyResult> UninstallAsync(UninstallCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.UninstallCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}