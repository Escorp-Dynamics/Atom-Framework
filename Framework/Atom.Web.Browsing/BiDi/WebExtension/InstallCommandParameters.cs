using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Provides parameters for the webExtension.install command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="InstallCommandParameters"/> class.
/// </remarks>
/// <param name="extension">The <see cref="ExtensionData"/> object describing the extension to install.</param>
public class InstallCommandParameters(ExtensionData extension) : CommandParameters<InstallCommandResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "webExtension.install";

    /// <summary>
    /// Gets or sets the data of the web extension to install.
    /// </summary>
    public ExtensionData ExtensionData { get; set; } = extension;
}