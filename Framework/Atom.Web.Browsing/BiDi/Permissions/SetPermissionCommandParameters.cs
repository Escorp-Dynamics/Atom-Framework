using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Permissions;

/// <summary>
/// Provides parameters for the browsingContext.activate command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SetPermissionCommandParameters"/> class.
/// </remarks>
/// <param name="descriptor">The descriptor of the permission to set.</param>
/// <param name="state">the state of the permission to set.</param>
/// <param name="origin">The origin, usually a URL, for which the permission will be set.</param>
public class SetPermissionCommandParameters(PermissionDescriptor descriptor, PermissionState state, string origin) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetPermissionCommandParameters"/> class.
    /// </summary>
    /// <param name="permissionName">The name of the permission to set.</param>
    /// <param name="state">the state of the permission to set.</param>
    /// <param name="origin">The origin, usually a URL, for which the permission will be set.</param>
    public SetPermissionCommandParameters(string permissionName, PermissionState state, string origin) : this(new PermissionDescriptor(permissionName), state, origin) { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "permissions.setPermission";

    /// <summary>
    /// Gets or sets the descriptor of the permission to set.
    /// </summary>
    [JsonRequired]
    public PermissionDescriptor Descriptor { get; set; } = descriptor;

    /// <summary>
    /// Gets or sets the state of the permission to set.
    /// </summary>
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PermissionState State { get; set; } = state;

    /// <summary>
    /// Gets or sets the origin, usually a URL, for which to set the permission.
    /// </summary>
    [JsonRequired]
    public string Origin { get; set; } = origin;

    /// <summary>
    /// Gets or sets the ID of the user context for which to set the permission.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserContextId { get; set; }
}