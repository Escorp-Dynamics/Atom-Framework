using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Abstract base class for script targets.
/// </summary>
[JsonConverter(typeof(ScriptTargetJsonConverter))]
public abstract class Target { }