namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Позиция узла в документе.
/// </summary>
[Flags]
public enum DocumentPosition : ushort
{
    /// <summary>
    /// Неизвестно.
    /// </summary>
    None = 0x00,
    /// <summary>
    /// Оба узла находятся в разных документах или в разных деревьях одного документа.
    /// </summary>
    Disconnected = 0x01,
    /// <summary>
    /// Сопоставляемый узел предшествует узлу во время прямого обхода при поиске в глубину.
    /// </summary>
    Preceding = 0x02,
    /// <summary>
    /// Сопоставляемый узел следует после узла во время прямого обхода при поиске в глубину.
    /// </summary>
    Following = 0x04,
    /// <summary>
    /// Сопоставляемый узел является предком узла.
    /// </summary>
    Contains = 0x08,
    /// <summary>
    /// Сопоставляемый узел является потомком узла.
    /// </summary>
    ContainedBy = 0x10,
    /// <summary>
    /// Результат зависит от произвольного и/или специфичного для реализации поведения, и его переносимость не гарантируется.
    /// </summary>
    ImplementationSpecific = 0x20,
}