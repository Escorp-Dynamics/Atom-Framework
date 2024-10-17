using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// TODO.
/// </summary>
public interface IWindowOrWorkerGlobalScope
{
    /// <summary>
    /// TODO.
    /// </summary>
    /// <value></value>
    [ScriptMember(ScriptAccess.ReadOnly)]
    ITrustedTypePolicyFactory TrustedTypes { get; }
}