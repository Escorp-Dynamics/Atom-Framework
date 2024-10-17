using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет фильтр узлов.
/// </summary>
public interface INodeFilter
{
    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("FILTER_ACCEPT", ScriptAccess.ReadOnly)]
    public const FilterType Accept = FilterType.Accept;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("FILTER_REJECT", ScriptAccess.ReadOnly)]
    public const FilterType Reject = FilterType.Reject;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("FILTER_SKIP", ScriptAccess.ReadOnly)]
    public const FilterType Skip = FilterType.Skip;

    /// <summary>
    /// Все.
    /// </summary>
    [ScriptMember("SHOW_ALL", ScriptAccess.ReadOnly)]
    public const FilterShow All = FilterShow.All;

    /// <summary>
    /// Элементы.
    /// </summary>
    [ScriptMember("SHOW_ELEMENT", ScriptAccess.ReadOnly)]
    public const FilterShow Element = FilterShow.Element;

    /// <summary>
    /// Атрибуты.
    /// </summary>
    [ScriptMember("SHOW_ATTRIBUTE", ScriptAccess.ReadOnly)]
    public const FilterShow Attribute = FilterShow.Attribute;

    /// <summary>
    /// Текстовые узлы.
    /// </summary>
    [ScriptMember("SHOW_TEXT", ScriptAccess.ReadOnly)]
    public const FilterShow Text = FilterShow.Text;

    /// <summary>
    /// CDATASection.
    /// </summary>
    [ScriptMember("SHOW_CDATA_SECTION", ScriptAccess.ReadOnly)]
    public const FilterShow CDATASection = FilterShow.CDATASection;

    /// <summary>
    /// Ссылки сущностей.
    /// </summary>
    [ScriptMember("SHOW_ENTITY_REFERENCE", ScriptAccess.ReadOnly)]
    public const FilterShow EntityReference = FilterShow.EntityReference;

    /// <summary>
    /// Сущности.
    /// </summary>
    [ScriptMember("SHOW_ENTITY", ScriptAccess.ReadOnly)]
    public const FilterShow Entity = FilterShow.Entity;

    /// <summary>
    /// Инструкции со связанными целями.
    /// </summary>
    [ScriptMember("SHOW_PROCESSING_INSTRUCTION", ScriptAccess.ReadOnly)]
    public const FilterShow ProcessingInstruction = FilterShow.ProcessingInstruction;

    /// <summary>
    /// Узлы комментариев.
    /// </summary>
    [ScriptMember("SHOW_COMMENT", ScriptAccess.ReadOnly)]
    public const FilterShow Comment = FilterShow.Comment;

    /// <summary>
    /// Документы.
    /// </summary>
    [ScriptMember("SHOW_DOCUMENT", ScriptAccess.ReadOnly)]
    public const FilterShow Document = FilterShow.Document;

    /// <summary>
    /// Узлы типов документов.
    /// </summary>
    [ScriptMember("SHOW_DOCUMENT_TYPE", ScriptAccess.ReadOnly)]
    public const FilterShow DocumentType = FilterShow.DocumentType;

    /// <summary>
    /// Фрагменты документов.
    /// </summary>
    [ScriptMember("SHOW_DOCUMENT_FRAGMENT", ScriptAccess.ReadOnly)]
    public const FilterShow DocumentFragment = FilterShow.DocumentFragment;

    /// <summary>
    /// Примечания.
    /// </summary>
    [ScriptMember("SHOW_NOTATION", ScriptAccess.ReadOnly)]
    public const FilterShow Notation = FilterShow.Notation;

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="node"></param>
    [ScriptMember]
    FilterType AcceptNode(INode node);
}