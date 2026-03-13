namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Команда, отправляемая драйвером расширению для выполнения на вкладке.
/// </summary>
public enum BridgeCommand
{
    /// <summary>
    /// Перейти по адресу.
    /// </summary>
    Navigate,

    /// <summary>
    /// Выполнить JavaScript-код.
    /// </summary>
    ExecuteScript,

    /// <summary>
    /// Найти элемент на странице.
    /// </summary>
    FindElement,

    /// <summary>
    /// Найти все подходящие элементы на странице.
    /// </summary>
    FindElements,

    /// <summary>
    /// Выполнить действие над элементом (клик, ввод и т. д.).
    /// </summary>
    ElementAction,

    /// <summary>
    /// Получить атрибут или свойство элемента.
    /// </summary>
    GetElementProperty,

    /// <summary>
    /// Получить текущий URL страницы.
    /// </summary>
    GetUrl,

    /// <summary>
    /// Получить заголовок страницы.
    /// </summary>
    GetTitle,

    /// <summary>
    /// Получить HTML-содержимое страницы.
    /// </summary>
    GetContent,

    /// <summary>
    /// Сделать снимок экрана вкладки.
    /// </summary>
    CaptureScreenshot,

    /// <summary>
    /// Установить cookie.
    /// </summary>
    SetCookie,

    /// <summary>
    /// Получить cookies.
    /// </summary>
    GetCookies,

    /// <summary>
    /// Удалить cookies.
    /// </summary>
    DeleteCookies,

    /// <summary>
    /// Ожидать появления элемента.
    /// </summary>
    WaitForElement,

    /// <summary>
    /// Ожидать навигации.
    /// </summary>
    WaitForNavigation,

    /// <summary>
    /// Эмулировать устройство ввода.
    /// </summary>
    EmulateInput,

    /// <summary>
    /// Перехватить сетевой запрос.
    /// </summary>
    InterceptRequest,

    /// <summary>
    /// Закрыть вкладку.
    /// </summary>
    CloseTab,

    /// <summary>
    /// Открыть новую вкладку.
    /// </summary>
    OpenTab,

    /// <summary>
    /// Открыть новое окно браузера.
    /// </summary>
    OpenWindow,

    /// <summary>
    /// Закрыть окно браузера.
    /// </summary>
    CloseWindow,

    /// <summary>
    /// Активировать (переключиться на) вкладку.
    /// </summary>
    ActivateTab,

    /// <summary>
    /// Активировать (переключиться на) окно.
    /// </summary>
    ActivateWindow,

    /// <summary>
    /// Установить настройки изоляции для вкладки (UA, локаль, cookies).
    /// </summary>
    SetTabContext,

    /// <summary>
    /// Выполнить JavaScript-код во всех фреймах вкладки (включая cross-origin iframe).
    /// </summary>
    ExecuteScriptInFrames,

    /// <summary>
    /// Диагностика: получить статус порта для вкладки (debug-only).
    /// </summary>
    DebugPortStatus,
}
