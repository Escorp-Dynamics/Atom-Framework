using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет фабрику политик доверия.
/// </summary>
public class TrustedTypePolicyFactory : ITrustedTypePolicyFactory
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public ITrustedHTML EmptyHTML { get; } = new TrustedHTML();

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public ITrustedScript EmptyScript { get; } = new TrustedScript();

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public ITrustedTypePolicy? DefaultPolicy { get; }

    internal TrustedTypePolicyFactory() => DefaultPolicy = CreatePolicy("default");

    /// <inheritdoc/>
    [ScriptMember]
    public ITrustedTypePolicy CreatePolicy(string policyName, TrustedTypePolicyOptions policyOptions) => new TrustedTypePolicy(policyName, policyOptions ?? TrustedTypePolicyOptions.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public ITrustedTypePolicy CreatePolicy(string policyName) => CreatePolicy(policyName, TrustedTypePolicyOptions.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsHTML(object? value) => value is TrustedHTML;

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsScript(object? value) => value is TrustedScript;

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsScriptURL(object? value) => value is TrustedScriptURL;

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetAttributeType(string tagName, string attribute, string? elementNs, string? attrNs) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetAttributeType(string tagName, string attribute, string? elementNs) => GetAttributeType(tagName, attribute, elementNs, default);

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetAttributeType(string tagName, string attribute) => GetAttributeType(tagName, attribute, default);

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetPropertyType(string tagName, string property, string? elementNs) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetPropertyType(string tagName, string property) => GetPropertyType(tagName, property, default);
}