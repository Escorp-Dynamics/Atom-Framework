using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Свойства инициализации наблюдателя мутации.
/// </summary>
public class MutationObserverInit
{
    /// <summary>
    /// Определяет, произошла ли мутация в списке дочерних узлов.
    /// </summary>
    [ScriptMember("childList")]
    public bool IsChildList { get; set; }

    /// <summary>
    /// Определяет, произошла ли мутация в списке атрибутов.
    /// </summary>
    [ScriptMember("attributes")]
    public bool IsAttributes { get; set; }

    /// <summary>
    /// Определяет, произошла ли мутация в текстовом узле.
    /// </summary>
    [ScriptMember("characterData")]
    public bool IsCharacterData { get; set; }

    /// <summary>
    /// Определяет, произошла ли мутация в дочернем дереве.
    /// </summary>
    [ScriptMember("subtree")]
    public bool IsSubtree { get; set; }

    /// <summary>
    /// Определяет, произошла ли мутация в предыдущем значении атрибута.
    /// </summary>
    [ScriptMember("attributeOldValue")]
    public bool IsAttributeOldValue { get; set; }

    /// <summary>
    /// Определяет, произошла ли мутация в предыдущем значении текстового узла.
    /// </summary>
    [ScriptMember("characterDataOldValue")]
    public bool IsCharacterDataOldValue { get; set; }

    /// <summary>
    /// Фильтр атрибутов.
    /// </summary>
    [ScriptMember]
    public IEnumerable<string> AttributeFilter { get; set; } = [];

    /// <summary>
    /// Свойства инициализации по умолчанию.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    public static MutationObserverInit Default { get; } = new();
}