using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// The types of user prompts.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<UserPromptHandlerType>))]
public enum UserPromptHandlerType
{
    /// <summary>
    /// Handler accepts the user prompt.
    /// </summary>
    Accept,

    /// <summary>
    /// Handler dismisses the user prompt.
    /// </summary>
    Dismiss,

    /// <summary>
    /// Handler ignores the user prompt.
    /// </summary>
    Ignore,
}