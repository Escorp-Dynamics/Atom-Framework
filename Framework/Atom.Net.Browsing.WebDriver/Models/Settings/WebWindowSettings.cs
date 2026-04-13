using System.Drawing;
using System.Net;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает настройки окна браузера.
/// </summary>
public sealed class WebWindowSettings
{
    /// <summary>
    /// Получает прокси окна.
    /// </summary>
    public IWebProxy? Proxy { get; init; }

    /// <summary>
    /// Получает признак переопределения использования прокси для окна.
    /// </summary>
    public bool? UseProxy { get; init; }

    /// <summary>
    /// Получает или задаёт виртуальную мышь окна.
    /// Если не указана, окно наследует мышь браузера.
    /// </summary>
    public VirtualMouse? Mouse { get; set; }

    /// <summary>
    /// Получает или задаёт виртуальную клавиатуру окна.
    /// Если не указана, окно наследует клавиатуру браузера.
    /// </summary>
    public VirtualKeyboard? Keyboard { get; set; }

    /// <summary>
    /// Получает размер окна.
    /// </summary>
    public Size? Size { get; init; }

    /// <summary>
    /// Получает позицию окна.
    /// </summary>
    public Point? Position { get; init; }

    /// <summary>
    /// Получает устройство для эмуляции.
    /// </summary>
    public Device? Device { get; init; }
}