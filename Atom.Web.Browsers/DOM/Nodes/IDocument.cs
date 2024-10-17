using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет документ DOM.
/// </summary>
public interface IDocument : INode, INonElementParentNode, IParentNode, IXPathEvaluatorBase
{
    /// <summary>
    /// Имплементация DOM.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IDOMImplementation Implementation { get; }

    /// <summary>
    /// Текущий адрес страницы.
    /// </summary>
    [ScriptMember("URL", ScriptAccess.ReadOnly)]
    Uri Url { get; }

    /// <summary>
    /// Адрес пространства имён.
    /// </summary>
    [ScriptMember("documentURI", ScriptAccess.ReadOnly)]
    new Uri Uri { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string CompatMode { get; }

    /// <summary>
    /// Кодировка.
    /// </summary>
    [ScriptMember("characterSet", ScriptAccess.ReadOnly)]
    string CharSet { get; }

    /// <summary>
    /// Тип контента.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string ContentType { get; }

    /// <summary>
    /// Тип документа.
    /// </summary>
    [ScriptMember("doctype", ScriptAccess.ReadOnly)]
    IDocumentType? DocType { get; }

    /// <summary>
    /// Связанный элемент.
    /// </summary>
    [ScriptMember("documentElement", ScriptAccess.ReadOnly)]
    IElement? Element { get; }

    /// <summary>
    /// Возвращает коллекцию элементов по имени тега.
    /// </summary>
    /// <param name="qualifiedName">Имя тега.</param>
    [ScriptMember("getElementsByTagName")]
    IHTMLCollection GetElementsByTag(string qualifiedName);

    /// <summary>
    /// Возвращает коллекцию элементов по адресу пространства имён и имени тега.
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

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="localName">Локальное имя элемента.</param>
    /// <param name="options">Настройки создания элемента.</param>
    [ScriptMember]
    IElement CreateElement(string localName, ElementCreationOptions options);

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="localName">Локальное имя элемента.</param>
    /// <param name="value">TODO.</param>
    [ScriptMember]
    IElement CreateElement(string localName, string value) => CreateElement(localName, new ElementCreationOptions { Is = value });

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="localName">Локальное имя элемента.</param>
    [ScriptMember]
    IElement CreateElement(string localName) => CreateElement(localName, new ElementCreationOptions());

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="qualifiedName">Имя элемента.</param>
    /// <param name="options">Настройки создания элемента.</param>
    [ScriptMember("createElementNS")]
    IElement CreateElement(Uri? namespaceURI, string qualifiedName, ElementCreationOptions options);

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя элемента.</param>
    /// <param name="value">TODO.</param>
    [ScriptMember("createElementNS")]
    IElement CreateElement(Uri? namespaceURI, string localName, string value) => CreateElement(namespaceURI, localName, new ElementCreationOptions { Is = value });

    /// <summary>
    /// Создаёт новый элемент.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя элемента.</param>
    [ScriptMember("createElementNS")]
    IElement CreateElement(Uri? namespaceURI, string localName) => CreateElement(namespaceURI, localName, new ElementCreationOptions());

    /// <summary>
    /// Создаёт фрагмент документа.
    /// </summary>
    [ScriptMember]
    IDocumentFragment CreateDocumentFragment();

    /// <summary>
    /// Создаёт текстовый узел.
    /// </summary>
    /// <param name="data">Данные текстового узла.</param>
    [ScriptMember]
    IText CreateTextNode(string data);

    /// <summary>
    /// Создаёт узел CDATASection.
    /// </summary>
    /// <param name="data">Данные узла.</param>
    [ScriptMember]
    ICDATASection CreateCDATASection(string data);

    /// <summary>
    /// Создаёт комментарий.
    /// </summary>
    /// <param name="data">Данные комментария.</param>
    [ScriptMember]
    IComment CreateComment(string data);

    /// <summary>
    /// Создаёт инструкцию со связанной целью.
    /// </summary>
    /// <param name="target">Цель инструкции.</param>
    /// <param name="data">Данные инструкции.</param>
    [ScriptMember]
    IProcessingInstruction CreateProcessingInstruction(string target, string data);

    /// <summary>
    /// Импортирует узел в документ.
    /// </summary>
    /// <param name="node">Импортируемый узел.</param>
    /// <param name="deep">TODO.</param>
    [ScriptMember]
    INode ImportNode(INode node, bool deep);

    /// <summary>
    /// Импортирует узел в документ.
    /// </summary>
    /// <param name="node">Импортируемый узел.</param>
    [ScriptMember]
    INode ImportNode(INode node) => ImportNode(node, default);

    /// <summary>
    /// Адаптирует узел для документа.
    /// </summary>
    /// <param name="node">Адаптируемый узел.</param>
    [ScriptMember]
    INode AdoptNode(INode node);

    /// <summary>
    /// Создаёт узел атрибута.
    /// </summary>
    /// <param name="localName">Локальное имя атрибута.</param>
    [ScriptMember]
    IAttr CreateAttribute(string localName);

    /// <summary>
    /// Создаёт узел атрибута.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="qualifiedName">Имя атрибута.</param>
    [ScriptMember("createAttributeNS")]
    IAttr CreateAttribute(Uri? namespaceURI, string qualifiedName);

    /// <summary>
    /// Создаёт новый диапазон.
    /// </summary>
    [ScriptMember]
    IRange CreateRange();

    /// <summary>
    /// Создаёт итератор для обхода узла.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    /// <param name="whatToShow">Фильтр отображения узлов.</param>
    /// <param name="filter">Фильтр узлов.</param>
    [ScriptMember]
    INodeIterator CreateNodeIterator(INode root, FilterShow whatToShow, INodeFilter? filter);

    /// <summary>
    /// Создаёт итератор для обхода узла.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    /// <param name="whatToShow">Фильтр отображения узлов.</param>
    [ScriptMember]
    INodeIterator CreateNodeIterator(INode root, FilterShow whatToShow) => CreateNodeIterator(root, whatToShow, default);

    /// <summary>
    /// Создаёт итератор для обхода узла.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    [ScriptMember]
    INodeIterator CreateNodeIterator(INode root) => CreateNodeIterator(root, FilterShow.All);

    /// <summary>
    /// Создаёт итератор для обхода дерева узлов.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    /// <param name="whatToShow">Фильтр отображения узлов.</param>
    /// <param name="filter">Фильтр узлов.</param>
    [ScriptMember]
    ITreeWalker CreateTreeWalker(INode root, FilterShow whatToShow, INodeFilter? filter);

    /// <summary>
    /// Создаёт итератор для обхода дерева узлов.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    /// <param name="whatToShow">Фильтр отображения узлов.</param>
    [ScriptMember]
    ITreeWalker CreateTreeWalker(INode root, FilterShow whatToShow) => CreateTreeWalker(root, whatToShow, default);

    /// <summary>
    /// Создаёт итератор для обхода дерева узлов.
    /// </summary>
    /// <param name="root">Ссылка на корневой узел.</param>
    [ScriptMember]
    ITreeWalker CreateTreeWalker(INode root) => CreateTreeWalker(root, FilterShow.All);
}