using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Log;

/// <summary>
/// The valid log levels for logging in the browser.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<LogLevel>))]
public enum LogLevel
{
    /// <summary>
    /// The debug log level.
    /// </summary>
    Debug,

    /// <summary>
    /// The info log level.
    /// </summary>
    Info,

    /// <summary>
    /// The warning log level.
    /// </summary>
    Warn,

    /// <summary>
    /// The error log level.
    /// </summary>
    Error,
}