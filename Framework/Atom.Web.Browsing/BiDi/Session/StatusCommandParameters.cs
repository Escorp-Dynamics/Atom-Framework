using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Provides parameters for the session.new command.
/// </summary>
public class StatusCommandParameters : CommandParameters<StatusCommandResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommandParameters"/> class.
    /// </summary>
    public StatusCommandParameters() { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "session.status";
}