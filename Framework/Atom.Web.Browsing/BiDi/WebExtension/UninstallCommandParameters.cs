using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Provides parameters for the webExtension.uninstall command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UninstallCommandParameters"/> class.
/// </remarks>
/// <param name="extensionId">The ID of the extension to uninstall.</param>
public class UninstallCommandParameters(string extensionId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "webExtension.uninstall";

    /// <summary>
    /// Gets or sets the data of the web extension to install.
    /// </summary>
    [JsonPropertyName("extension")]
    public string ExtensionId { get; set; } = extensionId;
}