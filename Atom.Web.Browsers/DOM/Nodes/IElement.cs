using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет узел элемента DOM.
/// </summary>
public interface IElement : INode, IParentNode, INonDocumentTypeChildNode, IChildNode, ISlottable
{
    /// <summary>
    /// Адрес пространства имён.
    /// </summary>
    [ScriptMember("namespaceURI", ScriptAccess.ReadOnly)]
    new Uri Uri { get; }

    /// <summary>
    /// Префикс пространства имён.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string? Prefix { get; }

    /// <summary>
    /// Локальное название.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string LocalName { get; }

    /// <summary>
    /// Имя тега.
    /// </summary>
    [ScriptMember("tagName", ScriptAccess.ReadOnly)]
    string Tag { get; }

    /// <summary>
    /// Идентификатор.
    /// </summary>
    [ScriptMember]
    string Id { get; set; }

    /// <summary>
    /// Название класса.
    /// </summary>
    [ScriptMember("className")]
    string Class { get; set; }

    /// <summary>
    /// Список классов.
    /// </summary>
    [ScriptMember("classList", ScriptAccess.ReadOnly)]
    IDOMTokenList Classes { get; }

    /// <summary>
    /// Слот.
    /// </summary>
    [ScriptMember]
    string Slot { get; set; }

    /// <summary>
    /// Атрибуты.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INamedNodeMap Attributes { get; }

    /// <summary>
    /// Теневое дерево.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IShadowRoot? ShadowRoot { get; }

    /// <summary>
    /// Возвращает коллекцию имён атрибутов.
    /// </summary>
    [ScriptMember("getAttributeNames", ScriptAccess.ReadOnly)]
    IEnumerable<string> AttributeNames { get; }

    /// <summary>
    /// Указывает, есть ли у элемента атрибуты.
    /// </summary>
    [ScriptMember]
    bool HasAttributes();

    /// <summary>
    /// Возвращает значение атрибута по его имени.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    [ScriptMember]
    string? GetAttribute(string qualifiedName);

    /// <summary>
    /// Возвращает значение атрибута по его адресу пространства имён и локальному имени.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя.</param>
    [ScriptMember("getAttributeNS")]
    string? GetAttribute(Uri? namespaceURI, string localName);

    /// <summary>
    /// Устанавливает значение атрибуту.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    /// <param name="value">Значение атрибута.</param>
    [ScriptMember]
    void SetAttribute(string qualifiedName, string value);

    /// <summary>
    /// Устанавливает значение атрибуту.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="qualifiedName">Имя атрибута.</param>
    /// <param name="value">Значение атрибута.</param>
    [ScriptMember("setAttributeNS")]
    void SetAttribute(Uri? namespaceURI, string qualifiedName, string value);

    /// <summary>
    /// Удаляет атрибут.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    [ScriptMember]
    void RemoveAttribute(string qualifiedName);

    /// <summary>
    /// Удаляет атрибут.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя атрибута.</param>
    [ScriptMember("removeAttributeNS")]
    void RemoveAttribute(Uri? namespaceURI, string localName);

    /// <summary>
    /// Добавляет атрибут, если его нет или удаляет, если он есть.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    /// <param name="force">Указывает, необходимо ли менять список атрибутов.</param>
    [ScriptMember]
    bool ToggleAttribute(string qualifiedName, bool force);

    /// <summary>
    /// Добавляет атрибут, если его нет или удаляет, если он есть.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    [ScriptMember]
    bool ToggleAttribute(string qualifiedName) => ToggleAttribute(qualifiedName, default);

    /// <summary>
    /// Определяет, есть ли атрибут.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    /// [ScriptMember]
    bool HasAttribute(string qualifiedName);

    /// <summary>
    /// Определяет, есть ли атрибут.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя атрибута.</param>
    [ScriptMember("hasAttributeNS")]
    bool HasAttribute(Uri? namespaceURI, string localName);

    /// <summary>
    /// Возвращает узел атрибута.
    /// </summary>
    /// <param name="qualifiedName">Имя атрибута.</param>
    [ScriptMember]
    IAttr? GetAttributeNode(string qualifiedName);

    /// <summary>
    /// Возвращает узел атрибута.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя атрибута.</param>
    [ScriptMember("getAttributeNodeNS")]
    IAttr? GetAttributeNode(Uri? namespaceURI, string localName);

    /// <summary>
    /// Задаёт узел атрибута.
    /// </summary>
    /// <param name="attr">Узел атрибута.</param>
    [ScriptMember]
    IAttr? SetAttributeNode(IAttr attr);

    /// <summary>
    /// Задаёт узел атрибута с учётом адреса пространства имён.
    /// </summary>
    /// <param name="attr">Узел атрибута.</param>
    [ScriptMember("setAttributeNodeNS")]
    IAttr? SetAttributeNodeByNS(IAttr attr);

    /// <summary>
    /// Удаляет узел атрибута.
    /// </summary>
    /// <param name="attr">Узел атрибута.</param>
    [ScriptMember]
    IAttr? RemoveAttributeNode(IAttr attr);

    /// <summary>
    /// Присоединяет теневое дерево.
    /// </summary>
    /// <param name="init">Параметры теневого дерева.</param>
    [ScriptMember]
    IShadowRoot AttachShadow(ShadowRootInit init);

    /// <summary>
    /// Возвращает ближайшего найденного родителя.
    /// </summary>
    /// <param name="selectors">Селектор узла.</param>
    [ScriptMember]
    IElement? Closest(string selectors);

    /// <summary>
    /// Определяет, соответствует ли узел элемента селектору.
    /// </summary>
    /// <param name="selectors">Селектор узла.</param>
    [ScriptMember]
    bool Matches(string selectors);

    /// <summary>
    /// Возвращает коллекцию элементов по имени тега.
    /// </summary>
    /// <param name="qualifiedName">Имя тега.</param>
    [ScriptMember("getElementsByTagName")]
    IHTMLCollection GetElementsByTag(string qualifiedName);

    /// <summary>
    /// Возвращает коллекцию элементов по имени тега.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя тега.</param>
    [ScriptMember("getElementsByTagNameNS")]
    IHTMLCollection GetElementsByTag(Uri? namespaceURI, string localName);

    /// <summary>
    /// Возвращает коллекцию элементов по имени класса.
    /// </summary>
    /// <param name="classNames">Имя класса.</param>
    [ScriptMember("getElementsByClassName")]
    IHTMLCollection GetElementsByClass(string classNames);
}