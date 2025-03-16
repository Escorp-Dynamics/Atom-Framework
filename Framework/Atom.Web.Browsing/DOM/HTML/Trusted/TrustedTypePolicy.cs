using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет политику доверия.
/// </summary>
public class TrustedTypePolicy : ITrustedTypePolicy
{
    private readonly TrustedTypePolicyOptions options;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string Name { get; }

    internal TrustedTypePolicy(string name, TrustedTypePolicyOptions options)
    {
        Name = name;
        this.options = options;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public ITrustedHTML CreateHTML(string input, params object?[] arguments)
    {
        if (options.CreateHTML is not null)
        {
            var processedInput = options.CreateHTML(input, arguments);
            if (processedInput is not null) return new TrustedHTML(processedInput);
        }

        return new TrustedHTML(input);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public ITrustedScript CreateScript(string input, params object?[] arguments)
    {
        if (options.CreateScript is not null)
        {
            var processedInput = options.CreateScript(input, arguments);
            if (processedInput is not null) return new TrustedScript(processedInput);
        }

        return new TrustedScript(input);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public ITrustedScriptURL CreateScriptURL(string input, params object?[] arguments)
    {
        if (options.CreateScriptURL is not null)
        {
            var processedInput = options.CreateScriptURL(input, arguments);
            if (processedInput is not null) return new TrustedScriptURL(processedInput);
        }

        return new TrustedScriptURL(input);
    }
}