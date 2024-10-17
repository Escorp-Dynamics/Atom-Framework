using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет именованный маппинг узлов.
/// </summary>
public interface INamedNodeMap : IEnumerable<IAttr>
{
    /// <summary>
    /// Количество узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Возвращает узел по его индексу.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IAttr? this[int index] { get; }

    /// <summary>
    /// Возвращает узел по его имени.
    /// </summary>
    /// <param name="qualifiedName">Имя узла.</param>
    [ScriptMember("getNamedItem")]
    IAttr? Get(string qualifiedName);

    /// <summary>
    /// Возвращает узел по его локальному имени и адресу пространства имён.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя.</param>
    [ScriptMember("getNamedItemNS")]
    IAttr? Get(Uri? namespaceURI, string localName);

    /// <summary>
    /// Добавляет узел.
    /// </summary>
    /// <param name="attr">Добавляемый атрибут.</param>
    [ScriptMember("setNamedItem")]
    IAttr? Set(IAttr attr);

    /// <summary>
    /// Добавляет узел по его адресу пространства имён.
    /// </summary>
    /// <param name="attr">Добавляемый атрибут.</param>
    [ScriptMember("setNamedItemNS")]
    IAttr? SetByNS(IAttr attr);

    /// <summary>
    /// Удаляет узел по его имени.
    /// </summary>
    /// <param name="qualifiedName">Имя узла.</param>
    [ScriptMember("removeNamedItem")]
    IAttr? Remove(string qualifiedName);

    /// <summary>
    /// Удаляет узел по его локальному имени и адресу пространства имён.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="localName">Локальное имя.</param>
    [ScriptMember("removeNamedItemNS")]
    IAttr? Remove(Uri? namespaceURI, string localName);
}