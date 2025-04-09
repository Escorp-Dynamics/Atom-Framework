using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет список токенов DOM.
/// </summary>
public interface IDOMTokenList : IEnumerable<string>
{
    /// <summary>
    /// Возвращает токен по его индексу.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string this[int index] { get; }

    /// <summary>
    /// Количество токенов в списке.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Суммарное значение токенов.
    /// </summary>
    [ScriptMember]
    string Value { get; set; }

    /// <summary>
    /// Определяет, содержится ли токен в списке.
    /// </summary>
    /// <param name="token">Искомый токен.</param>
    [ScriptMember]
    bool Contains(string token);

    /// <summary>
    /// Добавляет новые токены в список.
    /// </summary>
    /// <param name="tokens">Добавляемые токены.</param>
    [ScriptMember]
    void Add(params IEnumerable<string> tokens);

    /// <summary>
    /// Удаляет токены из списка.
    /// </summary>
    /// <param name="tokens">Удаляемые токены.</param>
    [ScriptMember]
    void Remove(params IEnumerable<string> tokens);

    /// <summary>
    /// Добавляет токен в список, если его нет или удаляет из списка, если он есть.
    /// </summary>
    /// <param name="token">Токен.</param>
    /// <param name="force">Указывает, нужно ли изменять исходный список.</param>
    [ScriptMember]
    bool Toggle(string token, bool force);

    /// <summary>
    /// Добавляет токен в список, если его нет или удаляет из списка, если он есть.
    /// </summary>
    /// <param name="token">Токен.</param>
    [ScriptMember]
    bool Toggle(string token) => Toggle(token, default);

    /// <summary>
    /// Заменяет токен.
    /// </summary>
    /// <param name="token">Исходный токен.</param>
    /// <param name="newToken">Новый токен.</param>
    [ScriptMember]
    bool Replace(string token, string newToken);

    /// <summary>
    /// Определяет, поддерживается ли токен.
    /// </summary>
    /// <param name="token">Токен.</param>
    [ScriptMember]
    bool Supports(string token);
}