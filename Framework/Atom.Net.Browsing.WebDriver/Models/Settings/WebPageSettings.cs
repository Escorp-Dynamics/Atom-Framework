using System.Net;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает настройки страницы.
/// </summary>
public sealed class WebPageSettings
{
    /// <summary>
    /// Получает прокси страницы.
    /// </summary>
    public IWebProxy? Proxy { get; init; }

    /// <summary>
    /// Получает признак переопределения использования прокси для страницы.
    /// </summary>
    public bool? UseProxy { get; init; }

    /// <summary>
    /// Получает или задаёт виртуальную мышь вкладки.
    /// Если не указана, вкладка наследует мышь окна.
    /// </summary>
    public VirtualMouse? Mouse { get; set; }

    /// <summary>
    /// Получает или задаёт виртуальную клавиатуру вкладки.
    /// Если не указана, вкладка наследует клавиатуру окна.
    /// </summary>
    public VirtualKeyboard? Keyboard { get; set; }

    /// <summary>
    /// Получает устройство для эмуляции.
    /// </summary>
    public Device? Device { get; init; }
}