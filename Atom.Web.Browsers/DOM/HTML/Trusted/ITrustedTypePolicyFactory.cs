using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет фабрику политик доверия.
/// </summary>
public interface ITrustedTypePolicyFactory
{
    /// <summary>
    /// Пустой HTML.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    ITrustedHTML EmptyHTML { get; }

    /// <summary>
    /// Пустой скрипт.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    ITrustedScript EmptyScript { get; }

    /// <summary>
    /// Политика по умолчанию.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    ITrustedTypePolicy? DefaultPolicy { get; }

    /// <summary>
    /// Создаёт политику.
    /// </summary>
    /// <param name="policyName">Название политики.</param>
    /// <param name="policyOptions">Настройки политики.</param>
    [ScriptMember]
    ITrustedTypePolicy CreatePolicy(string policyName, TrustedTypePolicyOptions policyOptions);

    /// <summary>
    /// Создаёт политику.
    /// </summary>
    /// <param name="policyName">Название политики.</param>
    [ScriptMember]
    ITrustedTypePolicy CreatePolicy(string policyName) => CreatePolicy(policyName, TrustedTypePolicyOptions.Default);

    /// <summary>
    /// Определяет, является ли объект HTML.
    /// </summary>
    /// <param name="value">Проверяемый объект.</param>
    [ScriptMember]
    bool IsHTML(object? value);

    /// <summary>
    /// Определяет, является ли объект скриптом.
    /// </summary>
    /// <param name="value">Проверяемый объект.</param>
    [ScriptMember]
    bool IsScript(object? value);

    /// <summary>
    /// Определяет, является ли объект скриптом в формате ссылки.
    /// </summary>
    /// <param name="value">Проверяемый объект.</param>
    [ScriptMember]
    bool IsScriptURL(object? value);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="attribute"></param>
    /// <param name="elementNs"></param>
    /// <param name="attrNs"></param>
    /// <returns></returns>
    [ScriptMember]
    string? GetAttributeType(string tagName, string attribute, string? elementNs, string? attrNs);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="attribute"></param>
    /// <param name="elementNs"></param>
    /// <returns></returns>
    [ScriptMember]
    string? GetAttributeType(string tagName, string attribute, string? elementNs) => GetAttributeType(tagName, attribute, elementNs, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="attribute"></param>
    /// <returns></returns>
    [ScriptMember]
    string? GetAttributeType(string tagName, string attribute) => GetAttributeType(tagName, attribute, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="property"></param>
    /// <param name="elementNs"></param>
    /// <returns></returns>
    [ScriptMember]
    string? GetPropertyType(string tagName, string property, string? elementNs);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="property"></param>
    /// <returns></returns>
    [ScriptMember]
    string? GetPropertyType(string tagName, string property) => GetPropertyType(tagName, property, default);
}