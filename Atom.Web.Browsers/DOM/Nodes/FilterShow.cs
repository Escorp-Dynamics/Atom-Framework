namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Режим отображения узлов.
/// </summary>
[Flags]
public enum FilterShow : ulong
{
    /// <summary>
    /// Не определено.
    /// </summary>
    None = 0,
    /// <summary>
    /// Элементы.
    /// </summary>
    Element = 0x1,
    /// <summary>
    /// Атрибуты.
    /// </summary>
    Attribute = 0x2,
    /// <summary>
    /// Текстовые узлы.
    /// </summary>
    Text = 0x4,
    /// <summary>
    /// CDATASection.
    /// </summary>
    CDATASection = 0x8,
    /// <summary>
    /// Ссылки сущностей.
    /// </summary>
    EntityReference = 0x10,
    /// <summary>
    /// Сущности.
    /// </summary>
    Entity = 0x20,
    /// <summary>
    /// Инструкции со связанными целями.
    /// </summary>
    ProcessingInstruction = 0x40,
    /// <summary>
    /// Узлы комментариев.
    /// </summary>
    Comment = 0x80,
    /// <summary>
    /// Документы.
    /// </summary>
    Document = 0x100,
    /// <summary>
    /// Узлы типов документов.
    /// </summary>
    DocumentType = 0x200,
    /// <summary>
    /// Фрагменты документов.
    /// </summary>
    DocumentFragment = 0x400,
    /// <summary>
    /// Примечания.
    /// </summary>
    Notation = 0x800,
    /// <summary>
    /// Все.
    /// </summary>
    All = 0xFFFFFFFF,
}