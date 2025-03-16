using Atom.Web.Browsing.DOM;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.BOM;

/// <summary>
/// Представляет локацию страницы.
/// </summary>
public interface ILocation
{
    /// <summary>
    /// Полный адрес страницы.
    /// </summary>
    [ScriptMember]
    Uri Href { get; set; }

    /// <summary>
    /// Оригинальный адрес страницы.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    Uri Origin { get; }

    /// <summary>
    /// Протокол (схема).
    /// </summary>
    [ScriptMember]
    string Protocol { get; set; }

    /// <summary>
    /// Хост.
    /// </summary>
    [ScriptMember]
    string Host { get; set; }

    /// <summary>
    /// Имя хоста.
    /// </summary>
    [ScriptMember("hostname")]
    string HostName { get; set; }

    /// <summary>
    /// Порт.
    /// </summary>
    [ScriptMember]
    string Port { get; set; }

    /// <summary>
    /// Путь.
    /// </summary>
    [ScriptMember("pathname")]
    string Path { get; set; }

    /// <summary>
    /// Поисковая строка.
    /// </summary>
    [ScriptMember]
    string Search { get; set; }

    /// <summary>
    /// Якорь.
    /// </summary>
    [ScriptMember]
    string Hash { get; set; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    IDOMStringList AncestorOrigins { get; }

    /// <summary>
    /// Назначает странице новый адрес без модификации DOM.
    /// </summary>
    /// <param name="url">Новый адрес.</param>
    [ScriptMember]
    void Assign(Uri url);

    /// <summary>
    /// Заменяет текущий адрес страницы.
    /// </summary>
    /// <param name="url">Новый адрес.</param>
    [ScriptMember]
    void Replace(Uri url);

    /// <summary>
    /// Перезагружает страницу.
    /// </summary>
    [ScriptMember]
    void Reload();
}