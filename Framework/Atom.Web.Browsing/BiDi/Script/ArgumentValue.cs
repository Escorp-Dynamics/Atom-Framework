using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Abstract base class for arguments used in scripts.
/// </summary>
[JsonDerivedType(typeof(LocalValue))]
[JsonDerivedType(typeof(RemoteReference))]
[JsonDerivedType(typeof(RemoteObjectReference))]
[JsonDerivedType(typeof(SharedReference))]
public abstract class ArgumentValue { }