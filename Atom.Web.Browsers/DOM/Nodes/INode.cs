using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет интерфейс, от которого наследуют несколько типов DOM, он так же позволяет различным типам быть обработанными (или протестированными).
/// </summary>
public interface INode : IEventTarget
{
    /// <summary>
    /// <see cref="IElement"/>.
    /// </summary>
    [ScriptMember("ELEMENT_NODE", ScriptAccess.ReadOnly)]
    public const NodeType ElementNode = NodeType.Element;

    /// <summary>
    /// <see cref="IAttr"/>.
    /// </summary>
    [ScriptMember("ATTRIBUTE_NODE", ScriptAccess.ReadOnly)]
    public const NodeType AttributeNode = NodeType.Attribute;

    /// <summary>
    /// <see cref="IText"/>.
    /// </summary>
    [ScriptMember("TEXT_NODE", ScriptAccess.ReadOnly)]
    public const NodeType TextNode = NodeType.Text;

    /// <summary>
    /// <see cref="ICDATASection"/>.
    /// </summary>
    [ScriptMember("CDATA_SECTION_NODE", ScriptAccess.ReadOnly)]
    public const NodeType CDATASectionNode = NodeType.CDATASection;

    /// <summary>
    /// <see cref="IProcessingInstruction"/>.
    /// </summary>
    [ScriptMember("PROCESSING_INSTRUCTION_NODE", ScriptAccess.ReadOnly)]
    public const NodeType ProcessingInstructionNode = NodeType.ProcessingInstruction;

    /// <summary>
    /// <see cref="IComment"/>.
    /// </summary>
    [ScriptMember("COMMENT_NODE", ScriptAccess.ReadOnly)]
    public const NodeType CommentNode = NodeType.Comment;

    /// <summary>
    /// <see cref="IDocument"/>.
    /// </summary>
    [ScriptMember("DOCUMENT_NODE", ScriptAccess.ReadOnly)]
    public const NodeType DocumentNode = NodeType.Document;

    /// <summary>
    /// <see cref="IDocumentType"/>.
    /// </summary>
    [ScriptMember("DOCUMENT_TYPE_NODE", ScriptAccess.ReadOnly)]
    public const NodeType DocumentTypeNode = NodeType.DocumentType;

    /// <summary>
    /// <see cref="IDocumentFragment"/>.
    /// </summary>
    [ScriptMember("DOCUMENT_FRAGMENT_NODE", ScriptAccess.ReadOnly)]
    public const NodeType DocumentFragmentNode = NodeType.DocumentFragment;

    /// <summary>
    /// Оба узла находятся в разных документах или в разных деревьях одного документа.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_DISCONNECTED", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionDisconnected = DocumentPosition.Disconnected;

    /// <summary>
    /// Сопоставляемый узел предшествует узлу во время прямого обхода при поиске в глубину.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_PRECEDING", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionPreceding = DocumentPosition.Preceding;

    /// <summary>
    /// Сопоставляемый узел следует после узла во время прямого обхода при поиске в глубину.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_FOLLOWING", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionFollowing = DocumentPosition.Following;

    /// <summary>
    /// Сопоставляемый узел является предком узла.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_CONTAINS", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionContains = DocumentPosition.Contains;

    /// <summary>
    /// Сопоставляемый узел является потомком узла.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_CONTAINED_BY", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionContainedBy = DocumentPosition.ContainedBy;

    /// <summary>
    /// Результат зависит от произвольного и/или специфичного для реализации поведения, и его переносимость не гарантируется.
    /// </summary>
    [ScriptMember("DOCUMENT_POSITION_IMPLEMENTATION_SPECIFIC", ScriptAccess.ReadOnly)]
    public const DocumentPosition DocumentPositionImplementationSpecific = DocumentPosition.ImplementationSpecific;

    /// <summary>
    /// Тип узла.
    /// </summary>
    [ScriptMember("nodeType", ScriptAccess.ReadOnly)]
    NodeType Type { get; }

    /// <summary>
    /// Название узла.
    /// </summary>
    [ScriptMember("nodeName", ScriptAccess.ReadOnly)]
    string Name { get; }

    /// <summary>
    /// Возвращает URL-адрес базового документа node для документа node document.
    /// </summary>
    [ScriptMember("baseURI", ScriptAccess.ReadOnly)]
    Uri Uri { get; }

    /// <summary>
    /// Возвращает значение <c>true</c>, если узел подключен; в противном случае значение <c>false</c>.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    bool IsConnected { get; }

    /// <summary>
    /// Возвращает <see cref="IDocument"/> к которому принадлежит этот узел. Если нет связанного с ним документа, возвращает <c>null</c>.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IDocument? OwnerDocument { get; }

    /// <summary>
    /// Возвращает <see cref="INode"/> который является родителем этого узла.
    /// Если нет такого узла, по причине того, что узел находится вверху древа или не относится к древу, данное свойство вернёт <c>null</c>.
    /// </summary>
    [ScriptMember("parentNode", ScriptAccess.ReadOnly)]
    INode? Parent { get; }

    /// <summary>
    /// Возвращает <see cref="IElement"/> который является родителем данного узла.
    /// Если узел не имеет родителя или если родитель не <see cref="IElement"/>, это свойство вернёт <c>null</c>.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? ParentElement { get; }

    /// <summary>
    /// Возвращает живой <see cref="NodeList"/>, содержащий всех потомков данного узла.
    /// Живой <see cref="NodeList"/> означает то, что если потомки узла изменяются, объект <see cref="NodeList"/> автоматически обновляется.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INodeList ChildNodes { get; }

    /// <summary>
    /// Возвращает <see cref="INode"/>, представляющий первый прямой узел потомок узла или <c>null</c>, если узел не имеет потомков.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode? FirstChild { get; }

    /// <summary>
    /// Возвращает <see cref="INode"/>, представляющий последний прямой узел потомок узла или <c>null</c>, если узел не имеет потомков.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode? LastChild { get; }

    /// <summary>
    /// Возвращает <see cref="INode"/> представляющий предыдущий узел древа или <c>null</c>, если нет такого узла.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode? PreviousSibling { get; }

    /// <summary>
    /// Возвращает <see cref="INode"/> представляющий следующий узел в древе или <c>null</c>, если не такого узла.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode? NextSibling { get; }

    /// <summary>
    /// Это строка, представляющая значение объектов.
    /// Для большинства типов <see cref="INode"/>, возвращает <c>null</c> и любой набор операция игнорируется.
    /// Для узлов типа TEXT_NODE (Text objects), COMMENT_NODE (Comment objects)
    /// и PROCESSING_INSTRUCTION_NODE (ProcessingInstruction objects), значение соответствует текстовым данным, содержащихся в объекте.
    /// </summary>
    [ScriptMember]
    string? NodeValue { get; set; }

    /// <summary>
    /// Текстовый контент элемента и всех его потомков.
    /// </summary>
    [ScriptMember]
    string? TextContent { get; set; }

    /// <summary>
    /// Указывает, есть ли у элемента дочерние узлы или нет.
    /// </summary>
    [ScriptMember]
    bool HasChildNodes();

    /// <summary>
    /// Проверяет, ссылаются ли два узла на один и тот же объект.
    /// </summary>
    /// <param name="otherNode">Узел для проверки.</param>
    [ScriptMember]
    bool IsSameNode(INode? otherNode);

    /// <summary>
    /// Возвращает корневой узел.
    /// </summary>
    /// <param name="options">Опции получения корневого узла.</param>
    [ScriptMember]
    INode GetRootNode(GetRootNodeOptions options);

    /// <summary>
    /// Возвращает корневой узел.
    /// </summary>
    [ScriptMember]
    INode GetRootNode() => GetRootNode(GetRootNodeOptions.Default);

    /// <summary>
    /// Преобразует указанный узел и все его под-деревья в "нормализованный" вид.
    /// В нормализованном под-дереве нет ни пустых, ни смежных текстовых узлов.
    /// </summary>
    [ScriptMember]
    void Normalize();

    /// <summary>
    /// Клонирует <see cref="INode"/>, и опционально, все его компоненты. По умолчанию, оно клонирует содержимое узла.
    /// </summary>
    /// <param name="deep">Указывает, требуется ли клонировать все компоненты <see cref="INode"/>.</param>
    [ScriptMember]
    INode CloneNode(bool deep);

    /// <summary>
    /// Клонирует <see cref="INode"/>.
    /// </summary>
    [ScriptMember]
    INode CloneNode() => CloneNode(default);

    /// <summary>
    /// Проверяет, равны ли два узла.
    /// Два узла равны, когда они имеют один и тот же тип, определяющий характеристики
    /// (для элементов это будет их идентификатор, количество потомков и т.д.),
    /// его атрибуты совпадают и т.д.
    /// Конкретный набор точек данных, которые должны совпадать, зависит от типов узлов.
    /// </summary>
    /// <param name="otherNode">Узел <see cref="INode"/> с которым надо сравнить.</param>
    [ScriptMember]
    bool IsEqualNode(INode? otherNode);

    /// <summary>
    /// Сообщает позицию переданного ему в качестве аргумента узла относительно узла, на котором он был вызван.
    /// </summary>
    /// <param name="other"><see cref="INode"/>, позиция которого должна быть найдена, относительно текущего узла.</param>
    /// <returns>Позиция относительно текущего узла.</returns>
    [ScriptMember]
    DocumentPosition CompareDocumentPosition(INode other);

    /// <summary>
    /// Указывает, является ли узел потомком данного узла, т.е. сам узел, один из его прямых потомков (<see cref="ChildNodes"/>),
    /// один из детей его детей и так далее.
    /// </summary>
    /// <param name="other">Элемент с которым производится сравнение.</param>
    [ScriptMember]
    bool Contains(INode? other);

    /// <summary>
    /// Возвращает строку, содержащую префикс для данного пространства имён URI, если он присутствует и <c>null</c> если нет.
    /// Когда возможно присутствие нескольких префиксов, результат зависит от реализации.
    /// </summary>
    /// <param name="namespaceUri">Исходное пространство имён.</param>
    [ScriptMember]
    string? LookupPrefix(Uri? namespaceUri);

    /// <summary>
    /// Берёт префикс и возвращает пространство имён URI связанное с ним в данном узле, если найден (и <c>null</c> если нет).
    /// Устанавливает <c>null</c> для префикса который возвращает пространство имён по умолчанию.
    /// </summary>
    /// <param name="prefix">Префикс пространства имён.</param>
    [ScriptMember]
    Uri? LookupNamespaceURI(string? prefix);

    /// <summary>
    /// Определяет, является ли заданное пространство имён пространством имён по умолчанию для данного узла.
    /// </summary>
    /// <param name="namespaceUri">Пространство имён.</param>
    [ScriptMember]
    bool IsDefaultNamespace(Uri? namespaceUri);

    /// <summary>
    /// Добавляет элемент в список дочерних элементов родителя перед указанным элементом.
    /// </summary>
    /// <param name="node">Элемент для вставки.</param>
    /// <param name="child">Элемент, перед которым будет вставлен newElement.</param>
    /// <returns>Вставленный элемент.</returns>
    [ScriptMember]
    INode InsertBefore(INode node, INode? child);

    /// <summary>
    /// Вставляет <see cref="INode"/> как последний дочерний узел данного элемента.
    /// </summary>
    /// <param name="node">Вставляемый <see cref="INode"/>.</param>
    [ScriptMember]
    INode AppendChild(INode node);

    /// <summary>
    /// Заменяет дочерний элемент на выбранный. Возвращает заменённый элемент.
    /// </summary>
    /// <param name="node">Элемент, на который будет заменён oldChild. В случает если он уже есть в DOM, то сначала он будет удалён.</param>
    /// <param name="child">Элемент, который будет заменён.</param>
    /// <returns>Заменённый элемент.</returns>
    [ScriptMember]
    INode ReplaceChild(INode node, INode child);

    /// <summary>
    /// Удаляет дочерний элемент из DOM. Возвращает удалённый элемент.
    /// </summary>
    /// <param name="child">Дочерний элемент который будет удалён из DOM.</param>
    /// <returns>Удалённый элемент.</returns>
    [ScriptMember]
    INode RemoveChild(INode child);
}