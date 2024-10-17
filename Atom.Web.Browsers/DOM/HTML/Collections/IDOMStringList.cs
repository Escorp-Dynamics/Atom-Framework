using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Интерфейс для работы с коллекцией строк DOM.
/// </summary>
public interface IDOMStringList : IEnumerable<string>
{
    /// <summary>
    /// Возвращает строку по указанному индексу.
    /// </summary>
    /// <param name="index">Индекс строки.</param>
    /// <returns>Строка по указанному индексу или <c>null</c>, если индекс вне диапазона.</returns>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string? this[int index] { get; }

    /// <summary>
    /// Возвращает количество строк в коллекции.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Проверяет, содержится ли указанная строка в коллекции.
    /// </summary>
    /// <param name="value">Строка для поиска.</param>
    /// <returns><c>true</c>, если строка содержится в коллекции, иначе <c>false</c>.</returns>
    [ScriptMember]
    bool Contains(string value);
}