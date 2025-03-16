using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Permissions;

/// <summary>
/// Values used for the granting or revoking of permissions.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<PermissionState>))]
public enum PermissionState
{
    /// <summary>
    /// The permission should be granted to the user.
    /// </summary>
    Granted,

    /// <summary>
    /// The permission should be denied to the user.
    /// </summary>
    Denied,

    /// <summary>
    /// The user should be prompted for permission access.
    /// </summary>
    Prompt,
}