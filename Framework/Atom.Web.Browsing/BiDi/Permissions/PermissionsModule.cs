namespace Atom.Web.Browsing.BiDi.Permissions;

/// <summary>
/// The Permissions module contains commands and events relating to browser permissions
/// as defined in the W3C Permissions specification (https://www.w3.org/TR/permissions/).
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PermissionsModule"/> class.
/// </remarks>
/// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
public sealed class PermissionsModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// The name of the permissions module.
    /// </summary>
    public const string PermissionsModuleName = "permissions";

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => PermissionsModuleName;

    /// <summary>
    /// Sets a permission for a given web site.
    /// </summary>
    /// <param name="commandProperties">The parameters for the command.</param>
    /// <returns>The result of the command containing a base64-encoded screenshot.</returns>
    public ValueTask<EmptyResult> SetPermissionAsync(SetPermissionCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetPermissionCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}