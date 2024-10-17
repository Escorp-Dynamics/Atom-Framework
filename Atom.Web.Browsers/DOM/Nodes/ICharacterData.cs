using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет собой объект <see cref="INode"/>, который содержит символы.
/// </summary>
public interface ICharacterData : INode, INonDocumentTypeChildNode, IChildNode
{
    /// <summary>
    /// Текстовые данные, которые содержит этот объект.
    /// </summary>
    [ScriptMember]
    string Data { get; set; }

    /// <summary>
    /// Длина строки в <see cref="Data"/>.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Возвращает подстроку.
    /// </summary>
    /// <param name="offset">Смещение подстроки.</param>
    /// <param name="count">Количество символов относительно смещения подстроки.</param>
    /// <returns>Подстрока.</returns>
    [ScriptMember("substringData")]
    string Substring(int offset, int count);

    /// <summary>
    /// Добавляет данные.
    /// </summary>
    /// <param name="data">Добавляемые данные</param>
    [ScriptMember("appendData")]
    void Append(string data);

    /// <summary>
    /// Помещает данные после указанного индекса.
    /// </summary>
    /// <param name="offset">Смещение.</param>
    /// <param name="data">Данные.</param>
    [ScriptMember("insertData")]
    void Insert(int offset, string data);

    /// <summary>
    /// Удаляет данные после указанного индекса.
    /// </summary>
    /// <param name="offset">Смещение.</param>
    /// <param name="count">Число удаляемых символов.</param>
    [ScriptMember("deleteData")]
    void Remove(int offset, int count);

    /// <summary>
    /// Заменяет подстроку.
    /// </summary>
    /// <param name="offset">Смещение подстроки.</param>
    /// <param name="count">Число символов подстроки.</param>
    /// <param name="data">Замещаемые данные.</param>
    [ScriptMember("replaceData")]
    void Replace(int offset, int count, string data);
}