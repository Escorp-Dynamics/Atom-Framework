using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет элемент HTML.
/// </summary>
public interface IHTMLElement : IElement
{
    /// <summary>
    /// Получает или задает заголовок элемента.
    /// </summary>
    [ScriptMember]
    string Title { get; set; }

    /// <summary>
    /// Получает или задает язык элемента.
    /// </summary>
    [ScriptMember("lang")]
    string Language { get; set; }

    /// <summary>
    /// Получает или задает флаг, указывающий, должен ли элемент быть переведен.
    /// </summary>
    [ScriptMember("translate")]
    bool IsTranslated { get; set; }

    /// <summary>
    /// Получает или задает направление текста элемента (ltr - слева направо, rtl - справа налево).
    /// </summary>
    [ScriptMember("dir")]
    string Direction { get; set; }

    /// <summary>
    /// Получает или задает флаг, указывающий, является ли элемент скрытым.
    /// </summary>
    [ScriptMember("hidden")]
    bool IsHidden { get; set; }

    /// <summary>
    /// Получает или задает флаг, указывающий, является ли элемент инертным.
    /// Инертный элемент не обрабатывает события мыши и не может получать фокус.
    /// </summary>
    [ScriptMember("inert")]
    bool IsInert { get; set; }

    /// <summary>
    /// Получает или задает доступный ключ элемента.
    /// </summary>
    [ScriptMember]
    string AccessKey { get; set; }

    /// <summary>
    /// Получает метку доступного ключа элемента.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string AccessKeyLabel { get; }

    /// <summary>
    /// Получает или задает флаг, указывающий, может ли элемент быть перетаскиваемым.
    /// </summary>
    [ScriptMember("draggable")]
    bool IsDraggable { get; set; }

    /// <summary>
    /// Получает или задает флаг, указывающий, должен ли элемент проверяться на орфографические ошибки.
    /// </summary>
    [ScriptMember("spellcheck")]
    bool IsSpellCheck { get; set; }

    /// <summary>
    /// Получает или задает предложения по написанию текста для элемента.
    /// </summary>
    [ScriptMember]
    string WritingSuggestions { get; set; }

    /// <summary>
    /// Получает или задает автоматическую капитализацию текста для элемента.
    /// </summary>
    [ScriptMember]
    string AutoCapitalize { get; set; }

    /// <summary>
    /// Получает или задает флаг, указывающий, должен ли элемент автоматически исправлять орфографические ошибки.
    /// </summary>
    [ScriptMember("autocorrect")]
    bool IsAutoCorrect { get; set; }

    /// <summary>
    /// Имитация клика на элементе.
    /// </summary>
    [ScriptMember]
    void Click();
}