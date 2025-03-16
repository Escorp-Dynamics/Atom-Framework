using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Permissions;

/// <summary>
/// Provides parameters for the browsingContext.activate command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PermissionDescriptor"/> class.
/// </remarks>
/// <param name="name">The name of the permission.</param>
public class PermissionDescriptor(string name)
{
    /// <summary>
    /// Gets or sets the name of the permission.
    /// </summary>
    [JsonRequired]
    [JsonPropertyName("name")]
    public string Name { get; set; } = name;
}