namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Тип события, инициированного расширением браузера.
/// </summary>
public enum BridgeEvent
{
    /// <summary>
    /// Вкладка подключена к мосту.
    /// </summary>
    TabConnected,

    /// <summary>
    /// Вкладка отключена от моста.
    /// </summary>
    TabDisconnected,

    /// <summary>
    /// Навигация на вкладке завершена.
    /// </summary>
    NavigationCompleted,

    /// <summary>
    /// DOM страницы полностью загружен.
    /// </summary>
    DomContentLoaded,

    /// <summary>
    /// Страница полностью загружена.
    /// </summary>
    PageLoaded,

    /// <summary>
    /// Консольное сообщение на вкладке.
    /// </summary>
    ConsoleMessage,

    /// <summary>
    /// Сетевой запрос перехвачен.
    /// </summary>
    RequestIntercepted,

    /// <summary>
    /// Сетевой ответ получен.
    /// </summary>
    ResponseReceived,

    /// <summary>
    /// Диалоговое окно появилось.
    /// </summary>
    DialogOpened,

    /// <summary>
    /// Ошибка JavaScript на странице.
    /// </summary>
    ScriptError,
}
